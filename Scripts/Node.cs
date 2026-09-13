using System;
using System.Collections.Generic;
using UnityEngine;

namespace XNode {
    /// <summary>
    /// 所有节点的基类。继承本类的类型会被 xNode 识别为合法节点。
    /// </summary>
    /// <example>
    /// <code>
    /// [System.Serializable]
    /// public class Adder : Node {
    ///     [Input] public float a;
    ///     [Input] public float b;
    ///     [Output] public float result;
    ///
    ///     // 有输出端口的节点应覆写 GetValue，为指定输出端口返回值
    ///     public override object GetValue(NodePort port) {
    ///         return a + b;
    ///     }
    /// }
    /// </code>
    /// </example>
    [Serializable]
    public abstract class Node : ScriptableObject {
        /// <summary> 控制 [Input]/[Output] 端口的后背字段值何时在节点上显示 </summary>
        public enum ShowBackingValue {
            /// <summary> 从不显示后背字段值 </summary>
            Never,
            /// <summary> 仅在端口没有任何连接时显示 </summary>
            Unconnected,
            /// <summary> 始终显示 </summary>
            Always
        }

        public enum ConnectionType {
            /// <summary> 允许多条连接 </summary>
            Multiple,
            /// <summary> 新连接总是顶掉旧连接 </summary>
            Override,
        }

        /// <summary> 约束端口允许连接的值类型 </summary>
        public enum TypeConstraint {
            /// <summary> 不做类型限制 </summary>
            None,
            /// <summary> 允许输入类型可从输出类型赋值的连接（如 ScriptableObject --> Object） </summary>
            Inherited,
            /// <summary> 只允许完全相同的类型 </summary>
            Strict,
            /// <summary> 允许输出类型可从输入类型赋值的连接（如 Object --> ScriptableObject） </summary>
            InheritedInverse,
            /// <summary> 允许任一方向可赋值的连接 </summary>
            InheritedAny
        }

        /// <summary> 遍历节点上的全部端口 </summary>
        public IEnumerable<NodePort> Ports { get { foreach (NodePort port in ports.Values) yield return port; } }
        /// <summary> 遍历全部输出端口 </summary>
        public IEnumerable<NodePort> Outputs { get { foreach (NodePort port in Ports) { if (port.IsOutput) yield return port; } } }
        /// <summary> 遍历全部输入端口 </summary>
        public IEnumerable<NodePort> Inputs { get { foreach (NodePort port in Ports) { if (port.IsInput) yield return port; } } }
        /// <summary> 遍历全部动态端口 </summary>
        public IEnumerable<NodePort> DynamicPorts { get { foreach (NodePort port in Ports) { if (port.IsDynamic) yield return port; } } }
        /// <summary> 遍历全部动态输出端口 </summary>
        public IEnumerable<NodePort> DynamicOutputs { get { foreach (NodePort port in Ports) { if (port.IsDynamic && port.IsOutput) yield return port; } } }
        /// <summary> 遍历全部动态输入端口 </summary>
        public IEnumerable<NodePort> DynamicInputs { get { foreach (NodePort port in Ports) { if (port.IsDynamic && port.IsInput) yield return port; } } }

        /// <summary> 把全部端口填入 results（先清空）；供编辑器每帧路径复用列表，避免迭代器分配 </summary>
        public void GetPorts(List<NodePort> results) {
            results.Clear();
            foreach (KeyValuePair<string, NodePort> pair in ports) results.Add(pair.Value);
        }

        /// <summary> 把全部输入端口填入 results（先清空） </summary>
        public void GetInputs(List<NodePort> results) {
            results.Clear();
            foreach (KeyValuePair<string, NodePort> pair in ports) if (pair.Value.IsInput) results.Add(pair.Value);
        }

        /// <summary> 把全部输出端口填入 results（先清空） </summary>
        public void GetOutputs(List<NodePort> results) {
            results.Clear();
            foreach (KeyValuePair<string, NodePort> pair in ports) if (pair.Value.IsOutput) results.Add(pair.Value);
        }

        /// <summary> 把全部动态端口填入 results（先清空） </summary>
        public void GetDynamicPorts(List<NodePort> results) {
            results.Clear();
            foreach (KeyValuePair<string, NodePort> pair in ports) if (pair.Value.IsDynamic) results.Add(pair.Value);
        }
        /// <summary> 所属的 <see cref="NodeGraph"/> </summary>
        [SerializeField] public NodeGraph graph;
        /// <summary> 在 <see cref="NodeGraph"/> 画布上的位置 </summary>
        [SerializeField] public Vector2 position;
        /// <summary> 不建议手动修改此字典；端口定义请使用 <see cref="InputAttribute"/> 和 <see cref="OutputAttribute"/> </summary>
        [SerializeField] private NodePortDictionary ports = new NodePortDictionary();

        /// <summary>
        /// 时序补丁：ScriptableObject.CreateInstance 在返回前就会触发 OnEnable，
        /// 此时 graph 尚未赋值，故通过该静态字段提前传入；OnEnable 内自动清空。
        /// </summary>
        public static NodeGraph graphHotfix;

        /// <summary>
        /// ScriptableObject 启用回调：从 <see cref="graphHotfix"/> 静态字段取回所属图引用
        /// （CreateInstance 返回前 OnEnable 就已触发，此时构造方还没来得及赋 graph），
        /// 随后按类字段同步端口并调用 <see cref="Init"/>。
        /// </summary>
        protected void OnEnable() {
            if (graphHotfix != null) graph = graphHotfix;
            graphHotfix = null;
            UpdatePorts();
            Init();
        }

        /// <summary> 让静态端口与 DynamicPortLists 管理的动态端口和类字段保持一致；节点启用或重绘动态端口列表时自动调用 </summary>
        public void UpdatePorts() {
            NodeDataCache.UpdatePorts(this, ports);
        }

        /// <summary> 节点初始化，在 OnEnable 时调用 </summary>
        protected virtual void Init() { }

        /// <summary> 校验所有端口连接，移除失效引用 </summary>
        public void VerifyConnections() {
            foreach (NodePort port in Ports) port.VerifyConnections();
        }

#region Dynamic Ports
        /// <summary> 添加动态输入端口 </summary>
        public NodePort AddDynamicInput(Type type, Node.ConnectionType connectionType = Node.ConnectionType.Multiple, Node.TypeConstraint typeConstraint = TypeConstraint.None, string fieldName = null) {
            return AddDynamicPort(type, NodePort.IO.Input, connectionType, typeConstraint, fieldName);
        }

        /// <summary> 添加动态输出端口 </summary>
        public NodePort AddDynamicOutput(Type type, Node.ConnectionType connectionType = Node.ConnectionType.Multiple, Node.TypeConstraint typeConstraint = TypeConstraint.None, string fieldName = null) {
            return AddDynamicPort(type, NodePort.IO.Output, connectionType, typeConstraint, fieldName);
        }

        /// <summary> 添加一个动态序列化端口 </summary>
        private NodePort AddDynamicPort(Type type, NodePort.IO direction, Node.ConnectionType connectionType = Node.ConnectionType.Multiple, Node.TypeConstraint typeConstraint = TypeConstraint.None, string fieldName = null) {
            if (fieldName == null) {
                fieldName = "dynamicInput_0";
                int i = 0;
                while (HasPort(fieldName)) fieldName = "dynamicInput_" + (++i);
            } else if (HasPort(fieldName)) {
                Debug.LogWarning("端口 '" + fieldName + "' 已存在于 " + name, this);
                return ports[fieldName];
            }
            NodePort port = new NodePort(fieldName, type, direction, connectionType, typeConstraint, this);
            ports.Add(fieldName, port);
            return port;
        }

        /// <summary> 按字段名移除动态端口 </summary>
        public void RemoveDynamicPort(string fieldName) {
            NodePort dynamicPort = GetPort(fieldName);
            if (dynamicPort == null) throw new ArgumentException("端口 " + fieldName + " 不存在");
            RemoveDynamicPort(dynamicPort);
        }

        /// <summary> 移除动态端口；静态端口不可移除 </summary>
        public void RemoveDynamicPort(NodePort port) {
            if (port == null) throw new ArgumentNullException("port");
            else if (port.IsStatic) throw new ArgumentException("不能移除静态端口");
            port.ClearConnections();
            ports.Remove(port.fieldName);
        }

        /// <summary> 移除节点上的全部动态端口 </summary>
        [ContextMenu("Clear Dynamic Ports")]
        public void ClearDynamicPorts() {
            List<NodePort> dynamicPorts = new List<NodePort>(DynamicPorts);
            foreach (NodePort port in dynamicPorts) {
                RemoveDynamicPort(port);
            }
        }
#endregion

#region Ports
        /// <summary> 按字段名取输出端口；不存在或方向不符返回 null </summary>
        public NodePort GetOutputPort(string fieldName) {
            NodePort port = GetPort(fieldName);
            if (port == null || port.direction != NodePort.IO.Output) return null;
            else return port;
        }

        /// <summary> 按字段名取输入端口；不存在或方向不符返回 null </summary>
        public NodePort GetInputPort(string fieldName) {
            NodePort port = GetPort(fieldName);
            if (port == null || port.direction != NodePort.IO.Input) return null;
            else return port;
        }

        /// <summary> 按字段名取端口；不存在返回 null </summary>
        public NodePort GetPort(string fieldName) {
            NodePort port;
            if (ports.TryGetValue(fieldName, out port)) return port;
            else return null;
        }

        /// <summary> 是否存在指定字段名的端口 </summary>
        public bool HasPort(string fieldName) {
            return ports.ContainsKey(fieldName);
        }
#endregion

#region Inputs/Outputs
        /// <summary> 按字段名取输入值；端口未连接时返回 fallback </summary>
        public T GetInputValue<T>(string fieldName, T fallback = default(T)) {
            NodePort port = GetPort(fieldName);
            if (port != null && port.Connection != null) return port.GetInputValue<T>();
            else return fallback;
        }

        /// <summary> 按字段名取全部输入值；端口未连接时返回 fallback </summary>
        public T[] GetInputValues<T>(string fieldName, params T[] fallback) {
            NodePort port = GetPort(fieldName);
            if (port != null && port.Connection != null) return port.GetInputValues<T>();
            else return fallback;
        }

        /// <summary> 为指定输出端口返回值；含输出端点的节点子类应覆写本方法 </summary>
        /// <param name="port">请求值的输出端口</param>
        public virtual object GetValue(NodePort port) {
            Debug.LogWarning("类型 " + GetType() + " 未覆写 GetValue(NodePort port)");
            return null;
        }
#endregion

        /// <summary> 两个端口建立连接后回调 </summary>
        /// <param name="from">输出侧</param> <param name="to">输入侧</param>
        public virtual void OnCreateConnection(NodePort from, NodePort to) { }

        /// <summary> 本节点的某端口连接被移除后回调 </summary>
        /// <param name="port">输出或输入端口</param>
        public virtual void OnRemoveConnection(NodePort port) { }

        /// <summary> 断开本节点的全部连接 </summary>
        public void ClearConnections() {
            foreach (NodePort port in Ports) port.ClearConnections();
        }

#region Attributes
        /// <summary> 把序列化字段标记为输入端口，通过 <see cref="GetInputPort(string)"/> 访问 </summary>
        [AttributeUsage(AttributeTargets.Field)]
        public class InputAttribute : Attribute {
            public ShowBackingValue backingValue;
            public ConnectionType connectionType;
            public bool dynamicPortList;
            public TypeConstraint typeConstraint;

            /// <summary> 把序列化字段标记为输入端口 </summary>
            /// <param name="backingValue">是否在节点上显示端口的后背字段值 </param>
            /// <param name="connectionType">是否允许多条连接 </param>
            /// <param name="typeConstraint">约束该端口允许哪些输入连接 </param>
            /// <param name="dynamicPortList">为 true 时显示为可排序的输入列表端口，自动为列表/数组的元素生成端口 </param>
            public InputAttribute(ShowBackingValue backingValue = ShowBackingValue.Unconnected, ConnectionType connectionType = ConnectionType.Multiple, TypeConstraint typeConstraint = TypeConstraint.None, bool dynamicPortList = false) {
                this.backingValue = backingValue;
                this.connectionType = connectionType;
                this.dynamicPortList = dynamicPortList;
                this.typeConstraint = typeConstraint;
            }
        }

        /// <summary> 把序列化字段标记为输出端口，通过 <see cref="GetOutputPort(string)"/> 访问 </summary>
        [AttributeUsage(AttributeTargets.Field)]
        public class OutputAttribute : Attribute {
            public ShowBackingValue backingValue;
            public ConnectionType connectionType;
            public bool dynamicPortList;
            public TypeConstraint typeConstraint;

            /// <summary> 把序列化字段标记为输出端口 </summary>
            /// <param name="backingValue">是否在节点上显示端口的后背字段值 </param>
            /// <param name="connectionType">是否允许多条连接 </param>
            /// <param name="typeConstraint">约束从该端口出发允许哪些连接 </param>
            /// <param name="dynamicPortList">为 true 时显示为可排序的输出列表端口，自动为列表/数组的元素生成端口 </param>
            public OutputAttribute(ShowBackingValue backingValue = ShowBackingValue.Never, ConnectionType connectionType = ConnectionType.Multiple, TypeConstraint typeConstraint = TypeConstraint.None, bool dynamicPortList = false) {
                this.backingValue = backingValue;
                this.connectionType = connectionType;
                this.dynamicPortList = dynamicPortList;
                this.typeConstraint = typeConstraint;
            }

            /// <summary> 旧版构造，缺省 TypeConstraint 参数 </summary>
            [Obsolete("Use constructor with TypeConstraint")]
            public OutputAttribute(ShowBackingValue backingValue, ConnectionType connectionType, bool dynamicPortList) : this(backingValue, connectionType, TypeConstraint.None, dynamicPortList) { }
        }

        /// <summary> 手动指定节点类型在右键创建菜单中的路径 </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class CreateNodeMenuAttribute : Attribute {
            public string menuName;
            public int order;
            /// <summary> 手动指定节点在右键菜单中的路径；menuName 为空则隐藏该节点 </summary>
            public CreateNodeMenuAttribute(string menuName) {
                this.menuName = menuName;
                this.order = 0;
            }

            /// <summary> 手动指定节点在右键菜单中的路径与排序 </summary>
            public CreateNodeMenuAttribute(string menuName, int order) {
                this.menuName = menuName;
                this.order = order;
            }
        }

        /// <summary> 限制同一类型节点在图中可添加的数量 </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class DisallowMultipleNodesAttribute : Attribute {
            // TODO: 让继承链共享配额：基类标注 [DisallowMultipleNodes(1)] 后，其派生类型与基类合计不超过配额
            public int max;
            /// <summary> 限制同一类型节点在图中可添加的数量；max 默认 1 </summary>
            public DisallowMultipleNodesAttribute(int max = 1) {
                this.max = max;
            }
        }

        /// <summary> 指定该节点类型的着色 </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class NodeTintAttribute : Attribute {
            public Color color;
            /// <summary> 按 [0.0f..1.0f] 浮点 RGB 指定节点颜色 </summary>
            public NodeTintAttribute(float r, float g, float b) {
                color = new Color(r, g, b);
            }

            /// <summary> 按 HEX 字符串指定节点颜色 </summary>
            public NodeTintAttribute(string hex) {
                ColorUtility.TryParseHtmlString(hex, out color);
            }

            /// <summary> 按 [0..255] 字节 RGB 指定节点颜色 </summary>
            public NodeTintAttribute(byte r, byte g, byte b) {
                color = new Color32(r, g, b, byte.MaxValue);
            }
        }

        /// <summary> 指定该节点类型的宽度 </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class NodeWidthAttribute : Attribute {
            public int width;
            /// <summary> 指定节点宽度 </summary>
            public NodeWidthAttribute(int width) {
                this.width = width;
            }
        }
#endregion

        /// <summary>
        /// 可序列化的端口字典：Unity 不序列化字典，这里用 keys/values 双列表手动同步。
        /// </summary>
        [Serializable] private class NodePortDictionary : Dictionary<string, NodePort>, ISerializationCallbackReceiver {
            [SerializeField] private List<string> keys = new List<string>();
            [SerializeField] private List<NodePort> values = new List<NodePort>();

            /// <summary>
            /// 序列化回调：把端口字典展开到 keys/values 两个平行列表，供 Unity 写入资产。
            /// 每次序列化都会先清空重建。
            /// </summary>
            public void OnBeforeSerialize() {
                keys.Clear();
                values.Clear();
                keys.Capacity = this.Count;
                values.Capacity = this.Count;
                foreach (KeyValuePair<string, NodePort> pair in this) {
                    keys.Add(pair.Key);
                    values.Add(pair.Value);
                }
            }

            /// <summary>
            /// 反序列化回调：从 keys/values 平行列表重建端口字典。
            /// 键值数量不一致说明资产数据损坏，按较短一侧截断加载并报错，避免整图加载失败。
            /// </summary>
            public void OnAfterDeserialize() {
                this.Clear();
#if UNITY_2021_3_OR_NEWER
                this.EnsureCapacity(keys.Count);
#endif
                // 键值数量不一致说明资产数据已损坏：记录错误并按较短一侧截断加载，保留可配对部分
                if (keys.Count != values.Count) {
                    Debug.LogError("端口字典反序列化后键值数量不一致（" + keys.Count + " 个键 / " + values.Count + " 个值），已按较短一侧截断加载。请确认键值类型均可序列化。");
                }
                int count = Math.Min(keys.Count, values.Count);
                for (int i = 0; i < count; i++)
                    this.Add(keys[i], values[i]);
            }
        }
    }
}
