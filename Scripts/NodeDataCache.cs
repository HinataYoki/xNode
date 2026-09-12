using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace XNode {
    /// <summary> 预缓存反射数据，避免运行期反复扫描类型与字段 </summary>
    public static class NodeDataCache {
        private static PortDataCache portDataCache;
        private static Dictionary<System.Type, Dictionary<string, string>> formerlySerializedAsCache;
        private static Dictionary<System.Type, string> typeQualifiedNameCache;
        /// <summary> 各节点类型上标记了 dynamicPortList 的字段名集合；UpdatePorts 每帧判定列表端口用，避免反射 </summary>
        private static Dictionary<System.Type, HashSet<string>> dynamicPortListFields;
        private static bool Initialized { get { return portDataCache != null; } }

        /// <summary> 取类型限定名并缓存，用于端口的类型按名反序列化 </summary>
        public static string GetTypeQualifiedName(System.Type type) {
            if (typeQualifiedNameCache == null) typeQualifiedNameCache = new Dictionary<System.Type, string>();

            string name;
            if (!typeQualifiedNameCache.TryGetValue(type, out name)) {
                name = type.AssemblyQualifiedName;
                typeQualifiedNameCache.Add(type, name);
            }
            return name;
        }

        /// <summary>
        /// 让节点端口与类字段定义保持一致：移除失效静态端口、补建新端口、同步动态列表端口设置。
        /// 会被编辑器每帧调用，端口已全部匹配时走零分配快路径直接返回。
        /// </summary>
        public static void UpdatePorts(Node node, Dictionary<string, NodePort> ports) {
            if (!Initialized) BuildCache();

            System.Type nodeType = node.GetType();

            Dictionary<string, NodePort> staticPorts;
            if (!portDataCache.TryGetValue(nodeType, out staticPorts)) {
                 staticPorts = new Dictionary<string, NodePort>();
            }

            // 快路径：现有端口与静态定义（含动态列表端口与其后背定义）完全一致时无需重建，
            // 覆盖编辑器每帧重复调用的大多数场景
            if (IsUpToDate(nodeType, ports, staticPorts)) return;

            Dictionary<string, List<NodePort>> removedPorts = new Dictionary<string, List<NodePort>>();

            Dictionary<string, string> formerlySerializedAs = null;
            if (formerlySerializedAsCache != null) formerlySerializedAsCache.TryGetValue(nodeType, out formerlySerializedAs);

            List<NodePort> dynamicListPorts = new List<NodePort>();

            // 清理现有端口字典：移除字段已不存在的静态端口，更新仍存在端口的类型；
            // 动态列表端口也要同步设置，保证序列化正确
            foreach (NodePort port in ports.Values.ToArray()) {
                NodePort staticPort;
                if (staticPorts.TryGetValue(port.fieldName, out staticPort)) {
                    // 端口存在但方向/连接类型/约束变了：移除后走补建流程；设置没变的动态端口不受影响
                    if (port.IsDynamic || port.direction != staticPort.direction || port.connectionType != staticPort.connectionType || port.typeConstraint != staticPort.typeConstraint) {
                        // 非动态且方向未变时，记录旧连接以便补建后尝试重连
                        if (!port.IsDynamic && port.direction == staticPort.direction) removedPorts.Add(port.fieldName, port.GetConnections());
                        port.ClearConnections();
                        ports.Remove(port.fieldName);
                    } else port.ValueType = staticPort.ValueType;
                }
                // 端口对应的字段已不存在：移除
                else if (port.IsStatic) {
                    // 字段带 FormerlySerializedAs 时，把旧字段名的连接记下来，补建阶段按新名字重连
                    string newName = null;
                    if (formerlySerializedAs != null && formerlySerializedAs.TryGetValue(port.fieldName, out newName)) removedPorts.Add(newName, port.GetConnections());

                    port.ClearConnections();
                    ports.Remove(port.fieldName);
                }
                // 受动态端口列表管理的动态端口：标记后续同步设置
                else if (IsDynamicListPort(nodeType, port.fieldName)) {
                    dynamicListPorts.Add(port);
                }
            }
            // 补建缺失的静态端口，并尝试恢复刚才记录的连接
            foreach (NodePort staticPort in staticPorts.Values) {
                if (!ports.ContainsKey(staticPort.fieldName)) {
                    NodePort port = new NodePort(staticPort, node);
                    List<NodePort> reconnectConnections;
                    if (removedPorts.TryGetValue(staticPort.fieldName, out reconnectConnections)) {
                        for (int i = 0; i < reconnectConnections.Count; i++) {
                            NodePort connection = reconnectConnections[i];
                            if (connection == null) continue;
                            // 注意：graphEditor.CanConnect 里自定义的特殊连接条件在此不会生效（这里只能用端口自身的
                            // CanConnectTo 判断）；仅在用户改端口类型且已有非标准 CanConnect 覆写的边缘场景下有影响
                            if (port.CanConnectTo(connection)) port.Connect(connection);
                        }
                    }
                    ports.Add(staticPort.fieldName, port);
                }
            }

            // 动态列表端口与后背端口的设置保持一致（新建端口会破坏编辑器，因此原地更新）
            foreach (NodePort listPort in dynamicListPorts) {
                string backingPortName = listPort.fieldName.Substring(0, listPort.fieldName.IndexOf(' '));
                NodePort backingPort = staticPorts[backingPortName];

                listPort.ValueType = GetBackingValueType(backingPort.ValueType);
                listPort.direction = backingPort.direction;
                listPort.connectionType = backingPort.connectionType;
                listPort.typeConstraint = backingPort.typeConstraint;
            }
        }

        /// <summary>
        /// 端口是否已与静态定义完全同步（零分配）：
        /// 数量一致，且每个端口要么是设置匹配的静态端口，要么是设置匹配后背定义的动态列表端口。
        /// </summary>
        private static bool IsUpToDate(System.Type nodeType, Dictionary<string, NodePort> ports, Dictionary<string, NodePort> staticPorts) {
            if (ports.Count != staticPorts.Count) return false;

            HashSet<string> listFields = null;
            bool hasListFields = dynamicPortListFields != null && dynamicPortListFields.TryGetValue(nodeType, out listFields);

            foreach (KeyValuePair<string, NodePort> pair in ports) {
                NodePort port = pair.Value;
                if (port == null) return false;

                NodePort staticPort;
                if (!staticPorts.TryGetValue(pair.Key, out staticPort)) {
                    // 动态端口：仅当它是设置与后背定义一致的动态列表端口时才算已同步
                    if (!port.IsDynamic || !hasListFields) return false;
                    if (!IsListPortMatchingBacking(listFields, pair.Key, port, staticPorts)) return false;
                    continue;
                }
                if (port.IsDynamic) return false;
                if (port.direction != staticPort.direction || port.connectionType != staticPort.connectionType || port.typeConstraint != staticPort.typeConstraint) return false;
                if (port.ValueType != staticPort.ValueType) return false;
            }
            return true;
        }

        /// <summary> 判断一个动态端口是否属于动态列表字段，且方向/连接类型/约束/元素类型都与后背定义一致 </summary>
        private static bool IsListPortMatchingBacking(HashSet<string> listFields, string fieldName, NodePort port, Dictionary<string, NodePort> staticPorts) {
            foreach (string backing in listFields) {
                // 字段名须形如 "<backing> <index>"；序号用手工解析避免 Substring 分配
                if (fieldName.Length <= backing.Length || fieldName[backing.Length] != ' ' || !fieldName.StartsWith(backing, System.StringComparison.Ordinal)) continue;

                int index = 0;
                bool valid = fieldName.Length > backing.Length + 1;
                for (int c = backing.Length + 1; valid && c < fieldName.Length; c++) {
                    char ch = fieldName[c];
                    if (ch < '0' || ch > '9') valid = false;
                    else index = index * 10 + (ch - '0');
                }
                if (!valid) continue;

                NodePort backingPort;
                if (!staticPorts.TryGetValue(backing, out backingPort)) continue;

                return port.direction == backingPort.direction
                    && port.connectionType == backingPort.connectionType
                    && port.typeConstraint == backingPort.typeConstraint
                    && port.ValueType == GetBackingValueType(backingPort.ValueType);
            }
            return false;
        }

        /// <summary>
        /// 取动态端口列表的元素类型（数组/List 的底层类型）；
        /// 不是数组也不是 List 时原样返回。
        /// </summary>
        private static System.Type GetBackingValueType(System.Type portValType) {
            if (portValType.HasElementType) {
                return portValType.GetElementType();
            }
            if (portValType.IsGenericType && portValType.GetGenericTypeDefinition() == typeof(List<>)) {
                return portValType.GetGenericArguments()[0];
            }
            return portValType;
        }

        /// <summary> 判断端口是否属于某个动态端口列表：查缓存集合做前缀匹配，零反射零字符串分配 </summary>
        private static bool IsDynamicListPort(System.Type nodeType, string fieldName) {
            HashSet<string> listFields;
            if (dynamicPortListFields == null || !dynamicPortListFields.TryGetValue(nodeType, out listFields)) return false;

            foreach (string backing in listFields) {
                if (fieldName.Length <= backing.Length || fieldName[backing.Length] != ' ' || !fieldName.StartsWith(backing, System.StringComparison.Ordinal)) continue;
                // 名字以 "<backing> " 开头且尾缀为纯数字才算列表端口
                bool valid = fieldName.Length > backing.Length + 1;
                for (int c = backing.Length + 1; valid && c < fieldName.Length; c++) {
                    if (fieldName[c] < '0' || fieldName[c] > '9') valid = false;
                }
                if (valid) return true;
            }
            return false;
        }

        /// <summary> 扫描程序集构建节点类型与端口缓存 </summary>
        private static void BuildCache() {
            portDataCache = new PortDataCache();
            dynamicPortListFields = new Dictionary<System.Type, HashSet<string>>();
            System.Type baseType = typeof(Node);
            List<System.Type> nodeTypes = new List<System.Type>();
            System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in assemblies) {
                // 跳过系统与 Unity 自带程序集（含子程序集，如 UnityEngine.UI），减少扫描量
                string assemblyName = assembly.GetName().Name;
                int index = assemblyName.IndexOf('.');
                if (index != -1) assemblyName = assemblyName.Substring(0, index);
                switch (assemblyName) {
                    case "UnityEditor":
                    case "UnityEngine":
                    case "Unity":
                    case "System":
                    case "mscorlib":
                    case "Microsoft":
                        continue;
                    default:
                        AddNodeTypesFromAssembly(assembly, baseType, nodeTypes);
                        break;
                }
            }

            for (int i = 0; i < nodeTypes.Count; i++) {
                CachePorts(nodeTypes[i]);
            }
        }

        /// <summary> 收集单个程序集里的全部非抽象 Node 子类；程序集部分类型加载失败时降级使用可加载部分 </summary>
        private static void AddNodeTypesFromAssembly(Assembly assembly, System.Type baseType, List<System.Type> nodeTypes) {
            System.Type[] types;
            try {
                types = assembly.GetTypes();
            } catch (ReflectionTypeLoadException ex) {
                types = ex.Types;
            } catch (System.Exception ex) {
                Debug.LogWarning("xNode 缓存端口时跳过程序集 '" + assembly.GetName().Name + "'：" + ex.Message);
                return;
            }

            // 大工程里部分程序集可能只能加载部分类型，跳过空项保证其它节点类型仍能进入缓存
            for (int i = 0; i < types.Length; i++) {
                System.Type type = types[i];
                if (type != null && !type.IsAbstract && baseType.IsAssignableFrom(type)) nodeTypes.Add(type);
            }
        }

        /// <summary> 取节点类型的全部序列化字段，含基类私有字段（GetFields 不返回继承的私有字段） </summary>
        public static List<FieldInfo> GetNodeFields(System.Type nodeType) {
            List<System.Reflection.FieldInfo> fieldInfo = new List<System.Reflection.FieldInfo>(nodeType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

            System.Type tempType = nodeType;
            while ((tempType = tempType.BaseType) != typeof(XNode.Node)) {
                FieldInfo[] parentFields = tempType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < parentFields.Length; i++) {
                    // 同名字段只保留派生类的一份
                    FieldInfo parentField = parentFields[i];
                    if (fieldInfo.TrueForAll(x => x.Name != parentField.Name)) {
                        fieldInfo.Add(parentField);
                    }
                }
            }
            return fieldInfo;
        }

        /// <summary>
        /// 扫描节点类型的全部字段（含基类私有字段），把带 [Input]/[Output] 特性的字段
        /// 构建成静态端口模板写入 portDataCache；同时登记 FormerlySerializedAs 旧字段名映射
        /// 与 dynamicPortList 字段名集合，供端口改名后重连与列表端口快速判定使用。
        /// </summary>
        private static void CachePorts(System.Type nodeType) {
            List<System.Reflection.FieldInfo> fieldInfo = GetNodeFields(nodeType);

            for (int i = 0; i < fieldInfo.Count; i++) {

                // 取 [Input]/[Output] 特性
                object[] attribs = fieldInfo[i].GetCustomAttributes(true);
                Node.InputAttribute inputAttrib = attribs.FirstOrDefault(x => x is Node.InputAttribute) as Node.InputAttribute;
                Node.OutputAttribute outputAttrib = attribs.FirstOrDefault(x => x is Node.OutputAttribute) as Node.OutputAttribute;
                UnityEngine.Serialization.FormerlySerializedAsAttribute formerlySerializedAsAttribute = attribs.FirstOrDefault(x => x is UnityEngine.Serialization.FormerlySerializedAsAttribute) as UnityEngine.Serialization.FormerlySerializedAsAttribute;

                if (inputAttrib == null && outputAttrib == null) continue;

                if (inputAttrib != null && outputAttrib != null) Debug.LogError("类型 " + nodeType.FullName + " 的字段 " + fieldInfo[i].Name + " 不能同时作为输入和输出。");
                else {
                    if (!portDataCache.ContainsKey(nodeType)) portDataCache.Add(nodeType, new Dictionary<string, NodePort>());
                     NodePort port = new NodePort(fieldInfo[i]);
                     portDataCache[nodeType].Add(port.fieldName, port);

                     // 记录动态列表字段，UpdatePorts 快路径据此零反射判定列表端口
                     if (inputAttrib != null && inputAttrib.dynamicPortList || outputAttrib != null && outputAttrib.dynamicPortList) {
                         HashSet<string> fields;
                         if (!dynamicPortListFields.TryGetValue(nodeType, out fields)) {
                             fields = new HashSet<string>();
                             dynamicPortListFields.Add(nodeType, fields);
                         }
                         fields.Add(port.fieldName);
                     }
                }

                if (formerlySerializedAsAttribute != null) {
                    if (formerlySerializedAsCache == null) formerlySerializedAsCache = new Dictionary<System.Type, Dictionary<string, string>>();
                    if (!formerlySerializedAsCache.ContainsKey(nodeType)) formerlySerializedAsCache.Add(nodeType, new Dictionary<string, string>());

                    if (formerlySerializedAsCache[nodeType].ContainsKey(formerlySerializedAsAttribute.oldName)) Debug.LogError("该节点上已存在另一个 FormerlySerializedAs 值 '" + formerlySerializedAsAttribute.oldName + "'。");
                    else formerlySerializedAsCache[nodeType].Add(formerlySerializedAsAttribute.oldName, fieldInfo[i].Name);
                }
            }
        }

        [System.Serializable]
        private class PortDataCache : Dictionary<System.Type, Dictionary<string, NodePort>> { }
    }
}
