using NUnit.Framework;
using UnityEngine;
using XNode;

namespace XNodeTests {
    /// <summary>
    /// 图容器测试：深拷贝独立性、连接重定向、节点身份与 graph 回指。
    /// </summary>
    public class NodeGraphTests {
        /// <summary> 直通节点：输出值等于输入值（未连接时 1） </summary>
        private class PassthroughNode : Node {
            [Input] public float a;
            [Output] public float b;
            public override object GetValue(NodePort port) {
                return port.fieldName == "b" ? GetInputValue<float>("a", 1f) : base.GetValue(port);
            }
        }

        private class InitReadsConnectionNode : PassthroughNode {
            protected override void Init() {
                // Clone 的 OnEnable/Init 阶段会先访问旧图连接，覆盖 Redirect 的缓存回归路径。
                if (GetInputPort("a").IsConnected) GetInputPort("a").Connection.ToString();
            }
        }

        private TestGraph graph;

        [SetUp]
        public void SetUp() {
            graph = ScriptableObject.CreateInstance<TestGraph>();
        }

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(graph);
        }

        [Test]
        public void AddNode_SetsGraphBackReference() {
            var a = graph.AddNode<PassthroughNode>();

            Assert.AreEqual(graph, a.graph);
            Assert.AreEqual(1, graph.nodes.Count);
        }

        [Test]
        public void Copy_ProducesIndependentGraphWithRedirectedConnections() {
            var a = graph.AddNode<PassthroughNode>();
            var b = graph.AddNode<PassthroughNode>();
            a.GetOutputPort("b").Connect(b.GetInputPort("a"));

            NodeGraph copy = graph.Copy();

            Assert.AreNotEqual(graph, copy);
            Assert.AreEqual(2, copy.nodes.Count);

            // 节点是新实例且 graph 指回新图
            Node copyA = copy.nodes[0];
            Node copyB = copy.nodes[1];
            Assert.AreNotEqual(a, copyA);
            Assert.AreNotEqual(b, copyB);
            Assert.AreEqual(copy, copyA.graph);
            Assert.AreEqual(copy, copyB.graph);

            // 连接已重定向到新节点
            Assert.IsTrue(copyA.GetOutputPort("b").IsConnectedTo(copyB.GetInputPort("a")));
            // 原图连接不受影响
            Assert.IsTrue(a.GetOutputPort("b").IsConnectedTo(b.GetInputPort("a")));
            Assert.AreEqual(copy.nodes[1], copyA.GetOutputPort("b").Connection.node);

            Object.DestroyImmediate(copy);
        }

        [Test]
        public void Copy_IsDeep_PositionIndependent() {
            var a = graph.AddNode<PassthroughNode>();
            a.position = new Vector2(12, 34);

            NodeGraph copy = graph.Copy();

            Assert.AreEqual(a.position, copy.nodes[0].position);
            // 修改副本不影响原图
            copy.nodes[0].position = new Vector2(56, 78);
            Assert.AreEqual(new Vector2(12, 34), a.position);

            Object.DestroyImmediate(copy);
        }

        [Test]
        public void CopyNode_ClearsConnectionsOnCopy() {
            var a = graph.AddNode<PassthroughNode>();
            var b = graph.AddNode<PassthroughNode>();
            a.GetOutputPort("b").Connect(b.GetInputPort("a"));

            Node copyB = graph.CopyNode(b);

            // 副本不带连接，但原图连接保持
            Assert.AreEqual(0, copyB.GetInputPort("a").ConnectionCount);
            Assert.IsTrue(a.GetOutputPort("b").IsConnectedTo(b.GetInputPort("a")));
            // a、b 与副本共 3 个节点
            Assert.AreEqual(3, graph.nodes.Count);
        }

        [Test]
        public void RemoveNode_RemovesFromList() {
            var a = graph.AddNode<PassthroughNode>();
            var b = graph.AddNode<PassthroughNode>();
            a.GetOutputPort("b").Connect(b.GetInputPort("a"));

            graph.RemoveNode(b);

            Assert.AreEqual(1, graph.nodes.Count);
            Assert.AreEqual(a, graph.nodes[0]);
            Assert.IsFalse(a.GetOutputPort("b").IsConnected);
        }

        [Test]
        public void RemoveNode_IgnoresNodeFromAnotherGraph() {
            TestGraph otherGraph = ScriptableObject.CreateInstance<TestGraph>();
            try {
                var source = graph.AddNode<PassthroughNode>();
                var target = graph.AddNode<PassthroughNode>();
                source.GetOutputPort("b").Connect(target.GetInputPort("a"));

                otherGraph.RemoveNode(source);

                Assert.AreEqual(2, graph.nodes.Count);
                Assert.IsTrue(source.GetOutputPort("b").IsConnectedTo(target.GetInputPort("a")));
            } finally {
                Object.DestroyImmediate(otherGraph);
            }
        }

        [Test]
        public void Clear_RemovesConnectionsBeforeClearingNodes() {
            var source = graph.AddNode<PassthroughNode>();
            var target = graph.AddNode<PassthroughNode>();
            source.GetOutputPort("b").Connect(target.GetInputPort("a"));

            graph.Clear();

            Assert.AreEqual(0, graph.nodes.Count);
            Assert.IsFalse(source.GetOutputPort("b").IsConnected);
            Assert.IsFalse(target.GetInputPort("a").IsConnected);
        }

        [Test]
        public void Copy_RedirectsConnectionsAfterInitAccess() {
            var source = graph.AddNode<PassthroughNode>();
            var target = graph.AddNode<InitReadsConnectionNode>();
            source.GetOutputPort("b").Connect(target.GetInputPort("a"));

            NodeGraph copy = graph.Copy();
            try {
                NodePort copiedInput = copy.nodes[1].GetInputPort("a");
                Assert.AreEqual(copy.nodes[0], copiedInput.Connection.node);
            } finally {
                Object.DestroyImmediate(copy);
            }
        }
    }
}
