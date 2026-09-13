using NUnit.Framework;
using UnityEngine;
using XNode;

namespace XNodeTests {
    /// <summary>
    /// 端口层测试：连接双向对称性、Override 语义、类型约束五档、求值环保护、动态端口增删。
    /// </summary>
    public class NodePortTests {
        /// <summary> 直通节点：输出值等于输入值（未连接时 1），用于构造连线和求值链 </summary>
        private class PassthroughNode : Node {
            [Input] public float a;
            [Output] public float b;
            public override object GetValue(NodePort port) {
                return port.fieldName == "b" ? GetInputValue<float>("a", 1f) : base.GetValue(port);
            }
        }

        /// <summary> 带动态列表输入字段的节点：列表端口的设置由 UpdatePorts 与后背字段保持同步 </summary>
        private class DynamicListNode : Node {
            [Input(dynamicPortList = true)] public float[] listIn;
            [Output] public float b;
        }

        /// <summary> 输入为 Override 语义的节点：新连接顶掉旧连接 </summary>
        private class OverrideInputNode : Node {
            [Input(connectionType: ConnectionType.Override)] public float a;
            [Output] public float b;
        }

        /// <summary> 承载各档类型约束的节点 </summary>
        private class ConstraintNode : Node {
            [Input] public Object noneIn;
            [Input(typeConstraint = TypeConstraint.Inherited)] public ScriptableObject inheritedIn;
            [Input(typeConstraint = TypeConstraint.Strict)] public float strictIn;
            [Input(typeConstraint = TypeConstraint.InheritedInverse)] public ScriptableObject inverseIn;
            [Input(typeConstraint = TypeConstraint.InheritedAny)] public ScriptableObject anyIn;
            [Output] public ScriptableObject scriptableOut;
            [Output] public float floatOut;
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
        public void Connect_IsSymmetric() {
            var a = graph.AddNode<PassthroughNode>();
            var b = graph.AddNode<PassthroughNode>();

            a.GetOutputPort("b").Connect(b.GetInputPort("a"));

            Assert.IsTrue(a.GetOutputPort("b").IsConnectedTo(b.GetInputPort("a")));
            Assert.IsTrue(b.GetInputPort("a").IsConnectedTo(a.GetOutputPort("b")));
            Assert.AreEqual(1, a.GetOutputPort("b").ConnectionCount);
            Assert.AreEqual(1, b.GetInputPort("a").ConnectionCount);
        }

        [Test]
        public void Disconnect_RemovesBothSides() {
            var a = graph.AddNode<PassthroughNode>();
            var b = graph.AddNode<PassthroughNode>();
            NodePort outPort = a.GetOutputPort("b");
            NodePort inPort = b.GetInputPort("a");
            outPort.Connect(inPort);

            outPort.Disconnect(inPort);

            Assert.IsFalse(outPort.IsConnected);
            Assert.IsFalse(inPort.IsConnected);
        }

        [Test]
        public void DisconnectByIndex_RemovesBothSides() {
            var a = graph.AddNode<PassthroughNode>();
            var b = graph.AddNode<PassthroughNode>();
            NodePort outPort = a.GetOutputPort("b");
            outPort.Connect(b.GetInputPort("a"));

            outPort.Disconnect(0);

            Assert.IsFalse(outPort.IsConnected);
            Assert.IsFalse(b.GetInputPort("a").IsConnected);
        }

        [Test]
        public void ClearConnections_RemovesAll() {
            var a = graph.AddNode<PassthroughNode>();
            var b = graph.AddNode<PassthroughNode>();
            var c = graph.AddNode<PassthroughNode>();
            a.GetOutputPort("b").Connect(b.GetInputPort("a"));
            a.GetOutputPort("b").Connect(c.GetInputPort("a"));

            a.GetOutputPort("b").ClearConnections();

            Assert.IsFalse(a.GetOutputPort("b").IsConnected);
            Assert.IsFalse(b.GetInputPort("a").IsConnected);
            Assert.IsFalse(c.GetInputPort("a").IsConnected);
        }

        [Test]
        public void OverrideConnection_ReplacesOldOne() {
            var a = graph.AddNode<PassthroughNode>();
            var c = graph.AddNode<PassthroughNode>();
            var target = graph.AddNode<OverrideInputNode>();

            a.GetOutputPort("b").Connect(target.GetInputPort("a"));
            c.GetOutputPort("b").Connect(target.GetInputPort("a"));

            // 新连接顶掉旧连接，且双向同步清除
            Assert.AreEqual(1, target.GetInputPort("a").ConnectionCount);
            Assert.IsTrue(target.GetInputPort("a").IsConnectedTo(c.GetOutputPort("b")));
            Assert.AreEqual(0, a.GetOutputPort("b").ConnectionCount);
        }

        [Test]
        public void CanConnectTo_RespectsTypeConstraints() {
            var node = graph.AddNode<ConstraintNode>();

            NodePort soOut = node.GetOutputPort("scriptableOut");
            NodePort fOut = node.GetOutputPort("floatOut");

            // None：不限制
            Assert.IsTrue(soOut.CanConnectTo(node.GetInputPort("noneIn")));
            // Inherited：输出类型需可赋给输入类型（ScriptableObject -> ScriptableObject 通过，float -> ScriptableObject 拒绝）
            Assert.IsTrue(soOut.CanConnectTo(node.GetInputPort("inheritedIn")));
            Assert.IsFalse(fOut.CanConnectTo(node.GetInputPort("inheritedIn")));
            // Strict：仅同型（float -> float 通过，ScriptableObject -> float 拒绝）
            Assert.IsTrue(fOut.CanConnectTo(node.GetInputPort("strictIn")));
            Assert.IsFalse(soOut.CanConnectTo(node.GetInputPort("strictIn")));
            // InheritedInverse：输入类型需可赋给输出类型
            Assert.IsTrue(soOut.CanConnectTo(node.GetInputPort("inverseIn")));
            // InheritedAny：任一方向可赋值（float 与 ScriptableObject 互不可赋，拒绝）
            Assert.IsTrue(soOut.CanConnectTo(node.GetInputPort("anyIn")));
            Assert.IsFalse(fOut.CanConnectTo(node.GetInputPort("anyIn")));
        }

        [Test]
        public void GetOutputValue_SelfLoopDoesNotOverflow() {
            var a = graph.AddNode<PassthroughNode>();
            // 输出直连自身输入形成环；环保护应截断求值而非栈溢出
            a.GetOutputPort("b").Connect(a.GetInputPort("a"));

            object result = a.GetOutputPort("b").GetOutputValue();

            // 求值被截断后 GetValue 拿不到 float 输入，返回 default(0)
            Assert.AreEqual(0f, result);
        }

        [Test]
        public void GetInputValue_UsesConnectedSource() {
            var a = graph.AddNode<PassthroughNode>();
            var b = graph.AddNode<PassthroughNode>();
            a.GetOutputPort("b").Connect(b.GetInputPort("a"));

            // b 的输入取 a 的输出；a 无上游输入，GetValue 走 fallback 1
            Assert.AreEqual(1f, b.GetInputPort("a").GetInputValue<float>());
        }

        [Test]
        public void DynamicPorts_AddAndRemove() {
            var a = graph.AddNode<PassthroughNode>();

            NodePort dyn = a.AddDynamicInput(typeof(float));
            Assert.IsTrue(dyn.IsDynamic);
            Assert.AreEqual(dyn, a.GetPort(dyn.fieldName));

            // 同名字段拒绝重复添加
            Assert.AreEqual(dyn, a.AddDynamicInput(typeof(float), fieldName: dyn.fieldName));

            a.RemoveDynamicPort(dyn);
            Assert.IsNull(a.GetPort(dyn.fieldName));

            // 静态端口不可移除
            Assert.Throws<System.ArgumentException>(() => a.RemoveDynamicPort(a.GetInputPort("a")));
        }

        [Test]
        public void UpdatePorts_KeepsDynamicListPortSettingsInSync() {
            var n = graph.AddNode<DynamicListNode>();
            // 模拟 DynamicPortList 添加的 "字段名 序号" 端口
            n.AddDynamicInput(typeof(float), Node.ConnectionType.Multiple, Node.TypeConstraint.None, "listIn 0");

            n.UpdatePorts();
            n.UpdatePorts(); // 第二次应命中快路径，端口不得丢失或被改

            NodePort port = n.GetPort("listIn 0");
            Assert.IsNotNull(port);
            Assert.IsTrue(port.IsDynamic);
            Assert.AreEqual(typeof(float), port.ValueType); // 数组元素类型
            Assert.AreEqual(NodePort.IO.Input, port.direction);
        }

        [Test]
        public void RemoveNode_ClearsConnections() {
            var a = graph.AddNode<PassthroughNode>();
            var b = graph.AddNode<PassthroughNode>();
            a.GetOutputPort("b").Connect(b.GetInputPort("a"));

            graph.RemoveNode(a);

            Assert.AreEqual(1, graph.nodes.Count);
            Assert.IsFalse(b.GetInputPort("a").IsConnected);
        }

        [Test]
        public void AddConnections_UsesSnapshotForOverrideTarget() {
            var upstreamA = graph.AddNode<PassthroughNode>();
            var upstreamB = graph.AddNode<PassthroughNode>();
            var sourceInput = graph.AddNode<PassthroughNode>();
            var overrideTarget = graph.AddNode<OverrideInputNode>();
            upstreamA.GetOutputPort("b").Connect(sourceInput.GetInputPort("a"));
            upstreamB.GetOutputPort("b").Connect(sourceInput.GetInputPort("a"));

            overrideTarget.GetInputPort("a").AddConnections(sourceInput.GetInputPort("a"));

            Assert.AreEqual(1, overrideTarget.GetInputPort("a").ConnectionCount);
            Assert.IsTrue(overrideTarget.GetInputPort("a").IsConnectedTo(upstreamB.GetOutputPort("b")));
        }

        [Test]
        public void ValueType_NullClearsSerializedTypeName() {
            var node = graph.AddNode<PassthroughNode>();
            NodePort port = node.GetInputPort("a");

            port.ValueType = null;

            Assert.IsNull(port.ValueType);
        }
    }
}
