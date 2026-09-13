using System;
using System.Collections.Generic;
using UnityEngine;

namespace XNode {
    /// <summary> 所有节点图的基类 </summary>
    [Serializable]
    public abstract class NodeGraph : ScriptableObject {

        /// <summary> 图中的全部节点 </summary>
        [SerializeField] public List<Node> nodes = new List<Node>();

        /// <summary> 按类型添加节点（便捷重载，转发到 System.Type 版本） </summary>
        public T AddNode<T>() where T : Node {
            return AddNode(typeof(T)) as T;
        }

        /// <summary> 按类型添加节点 </summary>
        public virtual Node AddNode(Type type) {
            if (type == null) throw new ArgumentNullException("type");
            if (!typeof(Node).IsAssignableFrom(type) || type.IsAbstract)
                throw new ArgumentException("节点类型必须是具体的 Node 子类", "type");

            Node node;
            Node.graphHotfix = this;
            try {
                node = ScriptableObject.CreateInstance(type) as Node;
            } finally {
                if (Node.graphHotfix == this) Node.graphHotfix = null;
            }
            if (node == null) throw new InvalidOperationException("无法创建节点 " + type.FullName);
            node.graph = this;
            nodes.Add(node);
            return node;
        }

        /// <summary> 在图中创建指定节点的副本 </summary>
        public virtual Node CopyNode(Node original) {
            if (original == null) throw new ArgumentNullException("original");
            Node.graphHotfix = this;
            Node node;
            try {
                node = ScriptableObject.Instantiate(original);
            } finally {
                if (Node.graphHotfix == this) Node.graphHotfix = null;
            }
            node.graph = this;
            node.ClearConnections();
            nodes.Add(node);
            return node;
        }

        /// <summary> 安全移除节点及其全部连接 </summary>
        /// <param name="node">要移除的节点</param>
        public virtual void RemoveNode(Node node) {
            if (node == null) throw new ArgumentNullException("node");
            if (!nodes.Contains(node)) return;
            node.ClearConnections();
            nodes.Remove(node);
            if (Application.isPlaying) Destroy(node);
        }

        /// <summary> 清空图中的全部节点和连接 </summary>
        public virtual void Clear() {
            for (int i = 0; i < nodes.Count; i++) {
                Node node = nodes[i];
                if (node == null) continue;
                node.ClearConnections();
                if (Application.isPlaying) Destroy(node);
            }
            nodes.Clear();
        }

        /// <summary> 创建本图的深拷贝（含全部节点与连接的重定向） </summary>
        public virtual XNode.NodeGraph Copy() {
            // 先实例化图本身，再逐个实例化节点；graphHotfix 保证节点 OnEnable 时能拿到新图引用
            NodeGraph graph = Instantiate(this);
            for (int i = 0; i < nodes.Count; i++) {
                if (nodes[i] == null) continue;
                Node.graphHotfix = graph;
                try {
                    Node node = Instantiate(nodes[i]) as Node;
                    node.graph = graph;
                    graph.nodes[i] = node;
                } finally {
                    if (Node.graphHotfix == graph) Node.graphHotfix = null;
                }
            }

            // 把节点连接从旧节点列表批量重定向到新节点列表
            for (int i = 0; i < graph.nodes.Count; i++) {
                if (graph.nodes[i] == null) continue;
                foreach (NodePort port in graph.nodes[i].Ports) {
                    port.Redirect(nodes, graph.nodes);
                }
            }

            return graph;
        }

        /// <summary> 图销毁回调：先清空全部节点再让自身销毁，Play 模式下同时 Destroy 各节点对象 </summary>
        protected virtual void OnDestroy() {
            // 图销毁前先清空节点，避免残留子资产
            Clear();
        }

#region Attributes
        /// <summary> 自动保证图中存在指定类型的节点，并阻止其被删除 </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
        public class RequireNodeAttribute : Attribute {
            public Type type0;
            public Type type1;
            public Type type2;

            /// <summary> 要求图中存在 1 个指定类型节点 </summary>
            public RequireNodeAttribute(Type type) {
                this.type0 = type;
                this.type1 = null;
                this.type2 = null;
            }

            /// <summary> 要求图中存在 2 个指定类型节点 </summary>
            public RequireNodeAttribute(Type type, Type type2) {
                this.type0 = type;
                this.type1 = type2;
                this.type2 = null;
            }

            /// <summary> 要求图中存在 3 个指定类型节点 </summary>
            public RequireNodeAttribute(Type type, Type type2, Type type3) {
                this.type0 = type;
                this.type1 = type2;
                this.type2 = type3;
            }

            /// <summary> 该要求是否覆盖指定类型 </summary>
            public bool Requires(Type type) {
                if (type == null) return false;
                if (type == type0) return true;
                else if (type == type1) return true;
                else if (type == type2) return true;
                return false;
            }
        }
#endregion
    }
}
