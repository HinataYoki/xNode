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
        private static bool Initialized { get { return portDataCache != null; } }

        /// <summary> 取类型限定名并缓存，用于端口的类型按名反序列化 </summary>
        public static string GetTypeQualifiedName(System.Type type) {
            if(typeQualifiedNameCache == null) typeQualifiedNameCache = new Dictionary<System.Type, string>();
            
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

            Dictionary<string, List<NodePort>> removedPorts = new Dictionary<string, List<NodePort>>();
            System.Type nodeType = node.GetType();

            Dictionary<string, string> formerlySerializedAs = null;
            if (formerlySerializedAsCache != null) formerlySerializedAsCache.TryGetValue(nodeType, out formerlySerializedAs);

            List<NodePort> dynamicListPorts = new List<NodePort>();

            Dictionary<string, NodePort> staticPorts;
            if (!portDataCache.TryGetValue(nodeType, out staticPorts)) {
                 staticPorts = new Dictionary<string, NodePort>();
            }            

            // Cleanup port dict - Remove nonexisting static ports - update static port types
            // AND update dynamic ports (albeit only those in lists) too, in order to enforce proper serialisation.
            // Loop through current node ports
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
                // If the port is dynamic and is managed by a dynamic port list, flag it for reference updates
                else if (IsDynamicListPort(port)) {
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
        /// Extracts the underlying types from arrays and lists, the only collections for dynamic port lists
        /// currently supported. If the given type is not applicable (i.e. if the dynamic list port was not
        /// defined as an array or a list), returns the given type itself.
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

        /// <summary>Returns true if the given port is in a dynamic port list.</summary>
        private static bool IsDynamicListPort(NodePort port) {
            // Ports flagged as "dynamicPortList = true" end up having a "backing port" and a name with an index, but we have
            // no guarantee that a dynamic port called "output 0" is an element in a list backed by a static "output" port.
            // Thus, we need to check for attributes... (but at least we don't need to look at all fields this time)
            string[] fieldNameParts = port.fieldName.Split(' ');
            if (fieldNameParts.Length != 2) return false;

            FieldInfo backingPortInfo = port.node.GetType().GetField(fieldNameParts[0]);
            if (backingPortInfo == null) return false;

            object[] attribs = backingPortInfo.GetCustomAttributes(true);
            return attribs.Any(x => {
                Node.InputAttribute inputAttribute = x as Node.InputAttribute;
                Node.OutputAttribute outputAttribute = x as Node.OutputAttribute;
                return inputAttribute != null && inputAttribute.dynamicPortList ||
                       outputAttribute != null && outputAttribute.dynamicPortList;
            });
        }

        /// <summary> 扫描程序集构建节点类型与端口缓存 </summary>
        private static void BuildCache() {
            portDataCache = new PortDataCache();
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
                        nodeTypes.AddRange(assembly.GetTypes().Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t)).ToArray());
                        break;
                }
            }

            for (int i = 0; i < nodeTypes.Count; i++) {
                CachePorts(nodeTypes[i]);
            }
        }

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

                if (inputAttrib != null && outputAttrib != null) Debug.LogError("Field " + fieldInfo[i].Name + " of type " + nodeType.FullName + " cannot be both input and output.");
                else {
                    if (!portDataCache.ContainsKey(nodeType)) portDataCache.Add(nodeType, new Dictionary<string, NodePort>());
                     NodePort port = new NodePort(fieldInfo[i]);
                     portDataCache[nodeType].Add(port.fieldName, port);
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
