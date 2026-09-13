using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace XNode {
    /// <summary>
    /// 节点端口。静态端口由字段的 [Input]/[Output] 特性生成，动态端口在运行时按需添加。
    /// 连接采用"两端各存一份引用"的冗余序列化，修改必须走 Connect/Disconnect 保持对称。
    /// </summary>
    [Serializable]
    public class NodePort {
        public enum IO { Input, Output }

        public int ConnectionCount { get { return connections == null ? 0 : connections.Count; } }

        /// <summary> 取第一个非空连接的对端端口，无连接时返回 null </summary>
        public NodePort Connection {
            get {
                if (connections == null) return null;
                for (int i = 0; i < connections.Count; i++) {
                    if (connections[i] != null) {
                        NodePort port = connections[i].Port;
                        if (port != null) return port;
                    }
                }
                return null;
            }
        }

        public IO direction {
            get { return _direction; }
            internal set { _direction = value; }
        }
        public Node.ConnectionType connectionType {
            get { return _connectionType; }
            internal set { _connectionType = value; }
        }
        public Node.TypeConstraint typeConstraint {
            get { return _typeConstraint; }
            internal set { _typeConstraint = value; }
        }

        /// <summary> 端口是否已连接任何对象 </summary>
        public bool IsConnected { get { return Connection != null; } }
        public bool IsInput { get { return direction == IO.Input; } }
        public bool IsOutput { get { return direction == IO.Output; } }

        public string fieldName { get { return _fieldName; } }
        public Node node { get { return _node; } }
        public bool IsDynamic { get { return _dynamic; } }
        public bool IsStatic { get { return !_dynamic; } }
        public Type ValueType {
            get {
                if (valueType == null && !string.IsNullOrEmpty(_typeQualifiedName)) valueType = Type.GetType(_typeQualifiedName, false);
                return valueType;
            }
            set {
                if (valueType == value && (value != null || string.IsNullOrEmpty(_typeQualifiedName))) return;
                valueType = value;
                _typeQualifiedName = value == null ? null : NodeDataCache.GetTypeQualifiedName(value);
            }
        }
        private Type valueType;

        [SerializeField] private string _fieldName;
        [SerializeField] private Node _node;
        [SerializeField] private string _typeQualifiedName;
        [SerializeField] private List<PortConnection> connections = new List<PortConnection>();
        [SerializeField] private IO _direction;
        [SerializeField] private Node.ConnectionType _connectionType;
        [SerializeField] private Node.TypeConstraint _typeConstraint;
        [SerializeField] private bool _dynamic;

        /// <summary> 由字段信息构造静态端口模板（不绑定节点），供 NodeDataCache 缓存反射结果 </summary>
        public NodePort(FieldInfo fieldInfo) {
            _fieldName = fieldInfo.Name;
            ValueType = fieldInfo.FieldType;
            _dynamic = false;
            var attribs = fieldInfo.GetCustomAttributes(false);
            for (int i = 0; i < attribs.Length; i++) {
                if (attribs[i] is Node.InputAttribute) {
                    _direction = IO.Input;
                    _connectionType = (attribs[i] as Node.InputAttribute).connectionType;
                    _typeConstraint = (attribs[i] as Node.InputAttribute).typeConstraint;
                } else if (attribs[i] is Node.OutputAttribute) {
                    _direction = IO.Output;
                    _connectionType = (attribs[i] as Node.OutputAttribute).connectionType;
                    _typeConstraint = (attribs[i] as Node.OutputAttribute).typeConstraint;
                }
                // 用特性覆盖端口值类型（配合 PortTypeOverride）
                if (attribs[i] is PortTypeOverrideAttribute) {
                    ValueType = (attribs[i] as PortTypeOverrideAttribute).type;
                }
            }
        }

        /// <summary> 复制一个端口并绑定到新节点（不复制连接），用于按模板创建静态端口 </summary>
        public NodePort(NodePort nodePort, Node node) {
            _fieldName = nodePort._fieldName;
            ValueType = nodePort.valueType;
            _direction = nodePort.direction;
            _dynamic = nodePort._dynamic;
            _connectionType = nodePort._connectionType;
            _typeConstraint = nodePort._typeConstraint;
            _node = node;
        }

        /// <summary> 构造动态端口：不受脚本重编译影响，适合运行期创建 </summary>
        public NodePort(string fieldName, Type type, IO direction, Node.ConnectionType connectionType, Node.TypeConstraint typeConstraint, Node node) {
            _fieldName = fieldName;
            this.ValueType = type;
            _direction = direction;
            _node = node;
            _dynamic = true;
            _connectionType = connectionType;
            _typeConstraint = typeConstraint;
        }

        /// <summary> 校验所有连接引用，移除指向已失效节点或端口的条目 </summary>
        public void VerifyConnections() {
            if (connections == null) connections = new List<PortConnection>();
            for (int i = connections.Count - 1; i >= 0; i--) {
                PortConnection connection = connections[i];
                NodePort connectedPort = connection == null ? null : connection.Port;
                if (connectedPort == null) {
                    connections.RemoveAt(i);
                    continue;
                }

                // 修复序列化或脚本重载造成的单边连接，保证两端仍然对称。
                if (!connectedPort.IsConnectedTo(this)) {
                    if (connectedPort.connections == null) connectedPort.connections = new List<PortConnection>();
                    connectedPort.connections.Add(new PortConnection(this));
                }
            }
        }

        // 求值递归深度与环告警状态；跨图共享，靠 try/finally 保证深度归零复位
        private static int getValueDepth;
        private static bool getValueCycleWarned;

        /// <summary> 调用所属节点的 GetValue 求输出值；带深度保护，端口连成环时截断并告警，避免栈溢出 </summary>
        public object GetOutputValue() {
            if (direction == IO.Input) return null;
            if (getValueDepth >= 512) {
                if (!getValueCycleWarned) {
                    getValueCycleWarned = true;
                    Debug.LogWarning("xNode: 节点求值深度超过 512，疑似存在循环连接，已截断求值。请检查节点 \"" + node.name + "\" 所在图的环。");
                }
                return null;
            }
            getValueDepth++;
            try {
                return node.GetValue(this);
            } finally {
                getValueDepth--;
                if (getValueDepth == 0) getValueCycleWarned = false;
            }
        }

        /// <summary> 取第一个连接端口的输出值；无连接或连接失效时返回 null </summary>
        public object GetInputValue() {
            NodePort connectedPort = Connection;
            if (connectedPort == null) return null;
            return connectedPort.GetOutputValue();
        }

        /// <summary> 取所有连接端口的输出值；顺带剔除失效连接 </summary>
        public object[] GetInputValues() {
            if (connections == null || connections.Count == 0) return new object[0];
            object[] objs = new object[connections.Count];
            int valueCount = 0;
            for (int i = 0; i < connections.Count; i++) {
                NodePort connectedPort = connections[i] == null ? null : connections[i].Port;
                if (connectedPort == null) {
                    connections.RemoveAt(i);
                    i--;
                    continue;
                }
                objs[valueCount++] = connectedPort.GetOutputValue();
            }
            if (valueCount != objs.Length) {
                object[] validObjs = new object[valueCount];
                Array.Copy(objs, validObjs, valueCount);
                return validObjs;
            }
            return objs;
        }

        /// <summary> 取第一个连接端口的输出值并转型；类型不符或无值时返回 default </summary>
        public T GetInputValue<T>() {
            object obj = GetInputValue();
            return obj is T ? (T) obj : default(T);
        }

        /// <summary> 取所有连接端口的输出值并转型；类型不符的条目保持 default </summary>
        public T[] GetInputValues<T>() {
            object[] objs = GetInputValues();
            T[] ts = new T[objs.Length];
            for (int i = 0; i < objs.Length; i++) {
                if (objs[i] is T) ts[i] = (T) objs[i];
            }
            return ts;
        }

        /// <summary> 尝试取第一个连接端口的输出值；返回是否存在类型匹配的值 </summary>
        public bool TryGetInputValue<T>(out T value) {
            object obj = GetInputValue();
            if (obj is T) {
                value = (T) obj;
                return true;
            } else {
                value = default(T);
                return false;
            }
        }

        /// <summary> 求所有 float 输入之和；无输入时返回 fallback，非 float 条目按 0 计 </summary>
        public float GetInputSum(float fallback) {
            object[] objs = GetInputValues();
            if (objs.Length == 0) return fallback;
            float result = 0;
            for (int i = 0; i < objs.Length; i++) {
                if (objs[i] is float) result += (float) objs[i];
            }
            return result;
        }

        /// <summary> 求所有 int 输入之和；无输入时返回 fallback，非 int 条目按 0 计 </summary>
        public int GetInputSum(int fallback) {
            object[] objs = GetInputValues();
            if (objs.Length == 0) return fallback;
            int result = 0;
            for (int i = 0; i < objs.Length; i++) {
                if (objs[i] is int) result += (int) objs[i];
            }
            return result;
        }

        /// <summary> 连接本端口与指定端口；自动处理 Override 语义、编辑器撤销，并触发两侧回调 </summary>
        public void Connect(NodePort port) {
            if (connections == null) connections = new List<PortConnection>();
            if (port == null) { Debug.LogWarning("不能连接到空端口"); return; }
            if (port.connections == null) port.connections = new List<PortConnection>();
            if (port == this) { Debug.LogWarning("端口不能连接自身"); return; }
            if (IsConnectedTo(port)) { Debug.LogWarning("端口已连接"); return; }
            if (direction == port.direction) { Debug.LogWarning("不能连接两个同为" + (direction == IO.Input ? "输入" : "输出") + "的端口"); return; }
            if (!CanConnectTo(port)) {
                Debug.LogWarning("端口类型不兼容，无法建立连接");
                return;
            }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(node, "Connect Port");
            UnityEditor.Undo.RecordObject(port.node, "Connect Port");
#endif
            // Override 语义：新连接顶掉旧连接
            if (port.connectionType == Node.ConnectionType.Override && port.ConnectionCount != 0) { port.ClearConnections(); }
            if (connectionType == Node.ConnectionType.Override && ConnectionCount != 0) { ClearConnections(); }
            connections.Add(new PortConnection(port));
            if (port.connections == null) port.connections = new List<PortConnection>();
            if (!port.IsConnectedTo(this)) port.connections.Add(new PortConnection(this));
            NodePort output = IsOutput ? this : port;
            NodePort input = IsInput ? this : port;
            output.node.OnCreateConnection(output, input);
            input.node.OnCreateConnection(output, input);
        }

        /// <summary> 取所有有效连接的对端端口列表；顺带剔除失效连接 </summary>
        public List<NodePort> GetConnections() {
            if (connections == null || connections.Count == 0) return new List<NodePort>();
            List<NodePort> result = new List<NodePort>(connections.Count);
            for (int i = 0; i < connections.Count; i++) {
                NodePort port = GetConnection(i);
                if (port != null) result.Add(port);
                else i--; // GetConnection 内部删除了失效条目，回退索引补位
            }
            return result;
        }

        /// <summary> 取第 i 个连接的对端端口；发现失效连接时顺带清理 </summary>
        public NodePort GetConnection(int i) {
            if (connections == null || i < 0 || i >= connections.Count || connections[i] == null) {
                if (connections != null && i >= 0 && i < connections.Count) connections.RemoveAt(i);
                return null;
            }
            if (connections[i].node == null) {
                connections.RemoveAt(i);
                return null;
            }
            NodePort port = connections[i].Port; // Port 属性带解析缓存，避免重复按字段名查表
            if (port == null) {
                connections.RemoveAt(i);
            }
            return port;
        }

        /// <summary> 取连接指定端口的那条连接在列表中的索引，不存在返回 -1 </summary>
        public int GetConnectionIndex(NodePort port) {
            if (connections == null) return -1;
            for (int i = 0; i < connections.Count; i++) {
                if (connections[i] != null && connections[i].Port == port) return i;
            }
            return -1;
        }

        /// <summary> 本端口是否已连接指定端口 </summary>
        public bool IsConnectedTo(NodePort port) {
            if (port == null || connections == null) return false;
            for (int i = 0; i < connections.Count; i++) {
                if (connections[i] != null && connections[i].Port == port) return true;
            }
            return false;
        }

        /// <summary> 判断本端口能否与指定端口相连（一进一出 + 双侧类型约束校验） </summary>
        public bool CanConnectTo(NodePort port) {
            if (port == null || port == this) return false;
            // 先分清输入输出侧；同向端口无法相连
            NodePort input = null, output = null;
            if (IsInput) input = this;
            else output = this;
            if (port.IsInput) input = port;
            else output = port;
            if (input == null || output == null) return false;
            // 输入、输出两侧的约束作用于同一对 (输入类型, 输出类型)，都需满足
            return MatchesConstraint(input.typeConstraint, input.ValueType, output.ValueType)
                && MatchesConstraint(output.typeConstraint, input.ValueType, output.ValueType);
        }

        /// <summary>
        /// 校验一对 (输入类型, 输出类型) 是否满足指定约束；None 恒通过。
        /// 端口连接校验与编辑器创建菜单的兼容性过滤共用此实现，避免两处语义漂移。
        /// </summary>
        public static bool MatchesConstraint(Node.TypeConstraint constraint, Type inputType, Type outputType) {
            if (constraint == Node.TypeConstraint.None) return true;
            if (inputType == null || outputType == null) return false;
            switch (constraint) {
                case Node.TypeConstraint.Inherited:
                    return inputType.IsAssignableFrom(outputType);
                case Node.TypeConstraint.Strict:
                    return inputType == outputType;
                case Node.TypeConstraint.InheritedInverse:
                    return outputType.IsAssignableFrom(inputType);
                case Node.TypeConstraint.InheritedAny:
                    return inputType.IsAssignableFrom(outputType) || outputType.IsAssignableFrom(inputType);
                default:
                    return true;
            }
        }

        /// <summary> 断开与指定端口的全部连接，双向同步移除并触发两侧回调 </summary>
        public void Disconnect(NodePort port) {
            if (connections == null || port == null) return;
            bool removed = false;
            // 移除本端指向对方的连接
            for (int i = connections.Count - 1; i >= 0; i--) {
                if (connections[i] != null && connections[i].Port == port) {
                    connections.RemoveAt(i);
                    removed = true;
                }
            }
            if (!removed) return;
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(node, "Disconnect Port");
            if (port.node != null) UnityEditor.Undo.RecordObject(port.node, "Disconnect Port");
#endif
            // 移除对方指回本端的连接并触发其回调
            if (port.connections != null) port.connections.RemoveAll(it => it != null && it.Port == this);
            port.node.OnRemoveConnection(port);
            node.OnRemoveConnection(this);
        }

        /// <summary> 按索引断开一条连接，双向同步移除并触发两侧回调 </summary>
        public void Disconnect(int i) {
            if (connections == null || i < 0 || i >= connections.Count) return;
            NodePort otherPort = connections[i] == null ? null : connections[i].Port;
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(node, "Disconnect Port");
            if (otherPort != null && otherPort.node != null) UnityEditor.Undo.RecordObject(otherPort.node, "Disconnect Port");
#endif
            connections.RemoveAt(i);
            node.OnRemoveConnection(this);
            if (otherPort != null) {
                if (otherPort.connections != null) otherPort.connections.RemoveAll(it => it != null && it.Port == this);
                otherPort.node.OnRemoveConnection(otherPort);
            }
        }

        /// <summary> 断开本端口全部连接 </summary>
        public void ClearConnections() {
            if (connections == null) return;
            while (connections.Count > 0) {
                NodePort port = connections[0] == null ? null : connections[0].Port;
                if (port == null) connections.RemoveAt(0);
                else Disconnect(port);
            }
        }

        /// <summary> 取指定连接的重路由点列表，仅用于编辑器布线整理 </summary>
        public List<Vector2> GetReroutePoints(int index) {
            return connections[index].reroutePoints;
        }

        /// <summary> 与另一端口互换全部连接（用于编辑器端口交换操作） </summary>
        public void SwapConnections(NodePort targetPort) {
            if (targetPort == null || targetPort == this) return;
            // 先快照两侧连接，清空后交叉重连，避免遍历中修改列表
            List<NodePort> portConnections = GetConnections();
            List<NodePort> targetPortConnections = targetPort.GetConnections();

            ClearConnections();
            targetPort.ClearConnections();

            for (int i = 0; i < portConnections.Count; i++)
                targetPort.Connect(portConnections[i]);

            for (int i = 0; i < targetPortConnections.Count; i++)
                Connect(targetPortConnections[i]);
        }

        /// <summary> 把目标端口的全部连接复制一份接到本端口 </summary>
        public void AddConnections(NodePort targetPort) {
            if (targetPort == null || targetPort.connections == null) return;
            List<NodePort> targetConnections = new List<NodePort>(targetPort.connections.Count);
            for (int i = 0; i < targetPort.connections.Count; i++) {
                if (targetPort.connections[i] != null) {
                    NodePort port = targetPort.connections[i].Port;
                    if (port != null) targetConnections.Add(port);
                }
            }
            for (int i = 0; i < targetConnections.Count; i++) Connect(targetConnections[i]);
        }

        /// <summary> 把本端口的全部连接迁移到目标端口；先断开再逐个重连，避免遍历中改双向连接导致漏移 </summary>
        public void MoveConnections(NodePort targetPort) {
            if (targetPort == null) throw new ArgumentNullException("targetPort");
            if (targetPort == this) return;

            List<NodePort> portConnections = GetConnections();

            ClearConnections();
            for (int i = 0; i < portConnections.Count; i++) {
                targetPort.Connect(portConnections[i]);
            }
        }

        /// <summary> 图深拷贝后，把旧节点列表的引用批量重定向到新节点列表 </summary>
        public void Redirect(List<Node> oldNodes, List<Node> newNodes) {
            if (connections == null) return;
            foreach (PortConnection connection in connections) {
                if (connection == null) continue;
                int index = oldNodes.IndexOf(connection.node);
                if (index >= 0) {
                    connection.node = newNodes[index];
                    // Instantiate 期间 Init 可能已解析并缓存旧图端口，重定向后必须刷新该缓存。
                    connection.RefreshPort();
                }
            }
        }

        /// <summary> 一条序列化的连接记录：目标字段名 + 目标节点引用 + 重路由点；Port 惰性解析并缓存 </summary>
        [Serializable]
        private class PortConnection {
            [SerializeField] public string fieldName;
            [SerializeField] public Node node;
            /// <summary>
            /// 连接的目标端口。首次访问时按 node + fieldName 解析并缓存到 port 字段，
            /// 之后复用缓存；节点或字段已失效时返回 null。序列化的是 node/fieldName，
            /// 本属性只存在于运行期。
            /// </summary>
            public NodePort Port { get { return port != null ? port : port = GetPort(); } }

            [NonSerialized] private NodePort port;
            /// <summary> 连线上的额外路径点，仅用于编辑器整理布线 </summary>
            [SerializeField] public List<Vector2> reroutePoints = new List<Vector2>();

            /// <summary>
            /// 从对端端口创建连接记录：立刻快照对端的节点引用与字段名（真正序列化的两样东西），
            /// 缓存的 port 引用只用于运行期加速。
            /// </summary>
            public PortConnection(NodePort port) {
                this.port = port;
                node = port.node;
                fieldName = port.fieldName;
            }

            /// <summary> 节点引用重定向后刷新运行时端口缓存 </summary>
            public void RefreshPort() {
                port = GetPort();
            }

            /// <summary> 按字段名解析出目标端口；节点或字段名失效时返回 null </summary>
            private NodePort GetPort() {
                if (node == null || string.IsNullOrEmpty(fieldName)) return null;
                return node.GetPort(fieldName);
            }
        }
    }
}
