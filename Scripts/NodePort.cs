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

        public int ConnectionCount { get { return connections.Count; } }

        /// <summary> 取第一个非空连接的对端端口，无连接时返回 null </summary>
        public NodePort Connection {
            get {
                for (int i = 0; i < connections.Count; i++) {
                    if (connections[i] != null) return connections[i].Port;
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
        public bool IsConnected { get { return connections.Count != 0; } }
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
                if (valueType == value) return;
                valueType = value;
                if (value != null) _typeQualifiedName = NodeDataCache.GetTypeQualifiedName(value);
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
                // Override ValueType of the Port
                if(attribs[i] is PortTypeOverrideAttribute) {
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
            for (int i = connections.Count - 1; i >= 0; i--) {
                if (connections[i].node != null &&
                    !string.IsNullOrEmpty(connections[i].fieldName) &&
                    connections[i].node.GetPort(connections[i].fieldName) != null)
                    continue;
                connections.RemoveAt(i);
            }
        }

        /// <summary> Return the output value of this node through its parent nodes GetValue override method. </summary>
        /// <returns> <see cref="Node.GetValue(NodePort)"/> </returns>
        public object GetOutputValue() {
            if (direction == IO.Input) return null;
            return node.GetValue(this);
        }

        /// <summary> 取第一个连接端口的输出值；无连接或连接失效时返回 null </summary>
        public object GetInputValue() {
            NodePort connectedPort = Connection;
            if (connectedPort == null) return null;
            return connectedPort.GetOutputValue();
        }

        /// <summary> 取所有连接端口的输出值；顺带剔除失效连接 </summary>
        public object[] GetInputValues() {
            object[] objs = new object[ConnectionCount];
            for (int i = 0; i < ConnectionCount; i++) {
                NodePort connectedPort = connections[i].Port;
                if (connectedPort == null) { // if we happen to find a null port, remove it and look again
                    connections.RemoveAt(i);
                    i--;
                    continue;
                }
                objs[i] = connectedPort.GetOutputValue();
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
            if (port == null) { Debug.LogWarning("Cannot connect to null port"); return; }
            if (port == this) { Debug.LogWarning("Cannot connect port to self."); return; }
            if (IsConnectedTo(port)) { Debug.LogWarning("Port already connected. "); return; }
            if (direction == port.direction) { Debug.LogWarning("Cannot connect two " + (direction == IO.Input ? "input" : "output") + " connections"); return; }
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
            node.OnCreateConnection(this, port);
            port.node.OnCreateConnection(this, port);
        }

        /// <summary> 取所有有效连接的对端端口列表；顺带剔除失效连接 </summary>
        public List<NodePort> GetConnections() {
            List<NodePort> result = new List<NodePort>();
            for (int i = 0; i < connections.Count; i++) {
                NodePort port = GetConnection(i);
                if (port != null) result.Add(port);
            }
            return result;
        }

        /// <summary> 取第 i 个连接的对端端口；发现失效连接时顺带清理 </summary>
        public NodePort GetConnection(int i) {
            //If the connection is broken for some reason, remove it.
            if (connections[i].node == null || string.IsNullOrEmpty(connections[i].fieldName)) {
                connections.RemoveAt(i);
                return null;
            }
            NodePort port = connections[i].node.GetPort(connections[i].fieldName);
            if (port == null) {
                connections.RemoveAt(i);
                return null;
            }
            return port;
        }

        /// <summary> 取连接指定端口的那条连接在列表中的索引，不存在返回 -1 </summary>
        public int GetConnectionIndex(NodePort port) {
            for (int i = 0; i < ConnectionCount; i++) {
                if (connections[i].Port == port) return i;
            }
            return -1;
        }

        /// <summary> 本端口是否已连接指定端口 </summary>
        public bool IsConnectedTo(NodePort port) {
            for (int i = 0; i < connections.Count; i++) {
                if (connections[i].Port == port) return true;
            }
            return false;
        }

        /// <summary> 判断本端口能否与指定端口相连（一进一出 + 双侧类型约束校验） </summary>
        public bool CanConnectTo(NodePort port) {
            // 先分清输入输出侧；同向端口无法相连
            NodePort input = null, output = null;
            if (IsInput) input = this;
            else output = this;
            if (port.IsInput) input = port;
            else output = port;
            if (input == null || output == null) return false;
            // Check input type constraints
            if (input.typeConstraint == XNode.Node.TypeConstraint.Inherited && !input.ValueType.IsAssignableFrom(output.ValueType)) return false;
            if (input.typeConstraint == XNode.Node.TypeConstraint.Strict && input.ValueType != output.ValueType) return false;
            if (input.typeConstraint == XNode.Node.TypeConstraint.InheritedInverse && !output.ValueType.IsAssignableFrom(input.ValueType)) return false;
            if (input.typeConstraint == XNode.Node.TypeConstraint.InheritedAny && !input.ValueType.IsAssignableFrom(output.ValueType) && !output.ValueType.IsAssignableFrom(input.ValueType)) return false;
            // Check output type constraints
            if (output.typeConstraint == XNode.Node.TypeConstraint.Inherited && !input.ValueType.IsAssignableFrom(output.ValueType)) return false;
            if (output.typeConstraint == XNode.Node.TypeConstraint.Strict && input.ValueType != output.ValueType) return false;
            if (output.typeConstraint == XNode.Node.TypeConstraint.InheritedInverse && !output.ValueType.IsAssignableFrom(input.ValueType)) return false;
            if (output.typeConstraint == XNode.Node.TypeConstraint.InheritedAny && !input.ValueType.IsAssignableFrom(output.ValueType) && !output.ValueType.IsAssignableFrom(input.ValueType)) return false;
            // Success
            return true;
        }

        /// <summary> Disconnect this port from another port </summary>
        public void Disconnect(NodePort port) {
            // 移除本端指向对方的连接
            for (int i = connections.Count - 1; i >= 0; i--) {
                if (connections[i].Port == port) {
                    connections.RemoveAt(i);
                }
            }
            if (port != null) {
                // Remove the other ports connection to this port
                for (int i = 0; i < port.connections.Count; i++) {
                    if (port.connections[i].Port == this) {
                        port.connections.RemoveAt(i);
                        // Trigger OnRemoveConnection from this side port
                        port.node.OnRemoveConnection(port);
                    }
                }
            }
            node.OnRemoveConnection(this);
        }

        /// <summary> 按索引断开一条连接，双向同步移除并触发两侧回调 </summary>
        public void Disconnect(int i) {
            NodePort otherPort = connections[i].Port;
            if (otherPort != null) {
                otherPort.connections.RemoveAll(it => { return it.Port == this; });
            }
            // Remove this ports connection to the other
            connections.RemoveAt(i);
            node.OnRemoveConnection(this);
            if (otherPort != null) otherPort.node.OnRemoveConnection(otherPort);
        }

        /// <summary> 断开本端口全部连接 </summary>
        public void ClearConnections() {
            while (connections.Count > 0) {
                Disconnect(connections[0].Port);
            }
        }

        /// <summary> 取指定连接的重路由点列表，仅用于编辑器布线整理 </summary>
        public List<Vector2> GetReroutePoints(int index) {
            return connections[index].reroutePoints;
        }

        /// <summary> 与另一端口互换全部连接（用于编辑器端口交换操作） </summary>
        public void SwapConnections(NodePort targetPort) {
            int aConnectionCount = connections.Count;
            int bConnectionCount = targetPort.connections.Count;

            List<NodePort> portConnections = new List<NodePort>();
            List<NodePort> targetPortConnections = new List<NodePort>();

            // Cache port connections
            for (int i = 0; i < aConnectionCount; i++)
                portConnections.Add(connections[i].Port);

            // Cache target port connections
            for (int i = 0; i < bConnectionCount; i++)
                targetPortConnections.Add(targetPort.connections[i].Port);

            ClearConnections();
            targetPort.ClearConnections();

            for (int i = 0; i < portConnections.Count; i++)
                targetPort.Connect(portConnections[i]);

            for (int i = 0; i < targetPortConnections.Count; i++)
                Connect(targetPortConnections[i]);
        }

        /// <summary> 把目标端口的全部连接复制一份接到本端口 </summary>
        public void AddConnections(NodePort targetPort) {
            int connectionCount = targetPort.ConnectionCount;
            for (int i = 0; i < connectionCount; i++) {
                PortConnection connection = targetPort.connections[i];
                NodePort otherPort = connection.Port;
                Connect(otherPort);
            }
        }

        /// <summary> 把本端口的全部连接迁移到目标端口；先断开再逐个重连，避免遍历中改双向连接导致漏移 </summary>
        public void MoveConnections(NodePort targetPort) {
            int connectionCount = connections.Count;

            // Add connections to target port
            for (int i = 0; i < connectionCount; i++) {
                PortConnection connection = targetPort.connections[i];
                NodePort otherPort = connection.Port;
                Connect(otherPort);
            }

            ClearConnections();
        }

        /// <summary> 图深拷贝后，把旧节点列表的引用批量重定向到新节点列表 </summary>
        public void Redirect(List<Node> oldNodes, List<Node> newNodes) {
            foreach (PortConnection connection in connections) {
                int index = oldNodes.IndexOf(connection.node);
                if (index >= 0) connection.node = newNodes[index];
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

            /// <summary> 按字段名解析出目标端口；节点或字段名失效时返回 null </summary>
            private NodePort GetPort() {
                if (node == null || string.IsNullOrEmpty(fieldName)) return null;
                return node.GetPort(fieldName);
            }
        }
    }
}
