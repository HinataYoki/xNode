using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if UNITY_2019_1_OR_NEWER && USE_ADVANCED_GENERIC_MENU
using GenericMenu = XNodeEditor.AdvancedGenericMenu;
#endif

namespace XNodeEditor {
    /// <summary> 自定义节点图编辑器的基类。继承它可覆盖图在编辑器中的绘制方式。 </summary>
    [CustomNodeGraphEditor(typeof(XNode.NodeGraph))]
    public class NodeGraphEditor : XNodeEditor.Internal.NodeEditorBase<NodeGraphEditor, NodeGraphEditor.CustomNodeGraphEditorAttribute, XNode.NodeGraph> {
        [Obsolete("Use window.position instead")]
        public Rect position { get { return window.position; } set { window.position = value; } }
        /// <summary> 当前是否正在重命名节点？ </summary>
        protected bool isRenaming;

        /// <summary> 节点绘制完成后的画布级自定义绘制钩子；在网格与节点之后执行 </summary>
        public virtual void OnGUI() { }

        /// <summary> 被 NodeEditorWindow 打开时调用 </summary>
        public virtual void OnOpen() { }

        /// <summary> NodeEditorWindow 获得焦点时调用 </summary>
        public virtual void OnWindowFocus() { }

        /// <summary> NodeEditorWindow 失去焦点时调用 </summary>
        public virtual void OnWindowFocusLost() { }

        /// <summary> 主网格平铺纹理，默认取偏好设置生成的网格纹理 </summary>
        public virtual Texture2D GetGridTexture() {
            return NodeEditorPreferences.GetSettings().gridTexture;
        }

        /// <summary> 网格交叉点纹理，与主网格叠绘形成大格小格 </summary>
        public virtual Texture2D GetSecondaryGridTexture() {
            return NodeEditorPreferences.GetSettings().crossTexture;
        }

        /// <summary> 返回该图类型的默认设置。用户从未保存过设置时将加载此设置。 </summary>
        public virtual NodeEditorPreferences.Settings GetDefaultPreferences() {
            return new NodeEditorPreferences.Settings();
        }

        /// <summary> 返回节点的上下文菜单路径。返回 null 或空字符串则隐藏该节点。 </summary>
        public virtual string GetNodeMenuName(Type type) {
            //检查类型是否带有 CreateNodeMenuAttribute
            XNode.Node.CreateNodeMenuAttribute attrib;
            if (NodeEditorUtilities.GetAttrib(type, out attrib)) // 返回自定义路径
                return attrib.menuName;
            else // 返回自动生成的路径
                return NodeEditorUtilities.NodeDefaultPath(type);
        }

        /// <summary> 菜单项的显示顺序。 </summary>
        public virtual int GetNodeMenuOrder(Type type) {
            //检查类型是否带有 CreateNodeMenuAttribute
            XNode.Node.CreateNodeMenuAttribute attrib;
            if (NodeEditorUtilities.GetAttrib(type, out attrib)) // 返回自定义路径
                return attrib.order;
            else
                return 0;
        }

        /// <summary>
        /// 在图视图中连接两个端口前调用，用于判断输出端口与输入端口是否兼容
        /// </summary>
        public virtual bool CanConnect(XNode.NodePort output, XNode.NodePort input) {
            return output.CanConnectTo(input);
        }

        /// <summary>
        /// 右键点击节点时向上下文菜单添加菜单项。
        /// 可重写此方法以添加自定义菜单项。
        /// </summary>
        /// <param name="menu"></param>
        /// <param name="compatibleType">用于筛选端口值类型与该类型兼容的节点</param>
        /// <param name="direction">兼容性的方向</param>
        public virtual void AddContextMenuItems(GenericMenu menu, XNode.NodePort nodePort = null, XNode.NodePort.IO direction = XNode.NodePort.IO.Input) {
            Vector2 pos = NodeEditorWindow.current.WindowToGridPosition(Event.current.mousePosition);
            Type compatibleType = nodePort?.ValueType;
            Type[] nodeTypes;

            if (compatibleType != null && NodeEditorPreferences.GetSettings().createFilter) {
                nodeTypes = NodeEditorUtilities.GetCompatibleNodesTypes(NodeEditorReflection.nodeTypes, compatibleType, nodePort.typeConstraint, direction).OrderBy(GetNodeMenuOrder).ToArray();
            } else {
                nodeTypes = NodeEditorReflection.nodeTypes.OrderBy(GetNodeMenuOrder).ToArray();
            }

            for (int i = 0; i < nodeTypes.Length; i++) {
                Type type = nodeTypes[i];

                //获取节点的上下文菜单路径
                string path = GetNodeMenuName(type);
                if (string.IsNullOrEmpty(path)) continue;

                //检查是否还允许添加更多该类型的节点
                XNode.Node.DisallowMultipleNodesAttribute disallowAttrib;
                bool disallowed = false;
                if (NodeEditorUtilities.GetAttrib(type, out disallowAttrib)) {
                    int typeCount = target.nodes.Count(x => x.GetType() == type);
                    if (typeCount >= disallowAttrib.max) disallowed = true;
                }

                //将节点条目添加到上下文菜单
                if (disallowed) menu.AddItem(new GUIContent(path), false, null);
                else menu.AddItem(new GUIContent(path), false, () => {
                    XNode.Node node = CreateNode(type, pos);
                    if (node != null) NodeEditorWindow.current.AutoConnect(node); //处理空节点以避免空引用异常
                });
            }
            menu.AddSeparator("");
            if (NodeEditorWindow.copyBuffer != null && NodeEditorWindow.copyBuffer.Length > 0) menu.AddItem(new GUIContent("Paste"), false, () => NodeEditorWindow.current.PasteNodes(pos));
            else menu.AddDisabledItem(new GUIContent("Paste"));
            menu.AddItem(new GUIContent("Preferences"), false, () => NodeEditorReflection.OpenPreferences());
            menu.AddCustomContextMenuItems(target);
        }

        /// <summary> Returned gradient is used to color noodles </summary>
        /// <param name="output"> The output this noodle comes from. Never null. </param>
        /// <param name="input"> The output this noodle comes from. Can be null if we are dragging the noodle. </param>
        public virtual Gradient GetNoodleGradient(XNode.NodePort output, XNode.NodePort input) {
            Gradient grad = new Gradient();

            //拖拽连线时绘制纯色并略微透明
            if (input == null) {
                Color a = GetTypeColor(output.ValueType);
                grad.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(a, 0f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(0.6f, 0f) }
                );
            }
            //正常连接时绘制从一个端口颜色渐变到另一个端口颜色
            else {
                Color a = GetTypeColor(output.ValueType);
                Color b = GetTypeColor(input.ValueType);
                //任一端口被悬停时向白色渲染
                if (window.hoveredPort == output || window.hoveredPort == input) {
                    a = Color.Lerp(a, Color.white, 0.8f);
                    b = Color.Lerp(b, Color.white, 0.8f);
                }
                grad.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                );
            }
            return grad;
        }

        /// <summary> 返回的浮点值用作连线粗细 </summary>
        /// <param name="output"> 连线的来源输出端口，永不为 null。 </param>
        /// <param name="input"> 连线的目标输入端口，拖拽连线时可为 null。 </param>
        public virtual float GetNoodleThickness(XNode.NodePort output, XNode.NodePort input) {
            return NodeEditorPreferences.GetSettings().noodleThickness;
        }

        /// <summary> 连线的路径样式（曲线/直线/折线/ShaderLab 风格），默认取偏好设置 </summary>
        public virtual NoodlePath GetNoodlePath(XNode.NodePort output, XNode.NodePort input) {
            return NodeEditorPreferences.GetSettings().noodlePath;
        }

        /// <summary> 连线描边（实线/虚线），默认取偏好设置 </summary>
        public virtual NoodleStroke GetNoodleStroke(XNode.NodePort output, XNode.NodePort input) {
            return NodeEditorPreferences.GetSettings().noodleStroke;
        }

        /// <summary> 返回的颜色用于给端口上色 </summary>
        public virtual Color GetPortColor(XNode.NodePort port) {
            return GetTypeColor(port.ValueType);
        }

        /// <summary>
        /// 返回的样式用于配置端口的内边距与图标纹理。
        /// 可通过这些属性自定义端口样式。
        ///
        /// 用到的属性有：
        /// <see cref="GUIStyle.padding"/>[Left 和 Right]、<see cref="GUIStyle.normal"/> [Background] = 边框纹理，
        /// 以及 <seealso cref="GUIStyle.active"/> [Background] = 圆点纹理；
        /// </summary>
        /// <param name="port">样式的归属端口</param>
        /// <returns></returns>
        public virtual GUIStyle GetPortStyle(XNode.NodePort port) {
            if (port.direction == XNode.NodePort.IO.Input)
                return NodeEditorResources.styles.inputPort;

            return NodeEditorResources.styles.outputPort;
        }

        /// <summary> 返回的颜色用于给端口背景上色。
        /// 通常用于外边缘效果 </summary>
        public virtual Color GetPortBackgroundColor(XNode.NodePort port) {
            return Color.gray;
        }

        /// <summary> 返回为某类型生成的颜色。该颜色可在偏好设置中修改 </summary>
        public virtual Color GetTypeColor(Type type) {
            return NodeEditorPreferences.GetTypeColor(type);
        }

        /// <summary> 重写此方法以显示自定义工具提示 </summary>
        public virtual string GetPortTooltip(XNode.NodePort port) {
            Type portType = port.ValueType;
            string tooltip = "";
            tooltip = portType.PrettyName();
            if (port.IsOutput) {
                object obj = port.node.GetValue(port);
                tooltip += " = " + (obj != null ? obj.ToString() : "null");
            }
            return tooltip;
        }

        /// <summary> 处理通过 DragAndDrop 拖入图中的对象 </summary>
        public virtual void OnDropObjects(UnityEngine.Object[] objects) {
            if (GetType() != typeof(NodeGraphEditor)) Debug.Log("No OnDropObjects override defined for " + GetType());
        }

        /// <summary> 创建节点并保存到图资源中 </summary>
        public virtual XNode.Node CreateNode(Type type, Vector2 position) {
            Undo.RecordObject(target, "Create Node");
            XNode.Node node = target.AddNode(type);
            if (node == null) return null; //处理空节点以避免空引用异常
            Undo.RegisterCreatedObjectUndo(node, "Create Node");
            node.position = position;
            if (node.name == null || node.name.Trim() == "") node.name = NodeEditorUtilities.NodeDefaultName(type);
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target))) AssetDatabase.AddObjectToAsset(node, target);
            if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
            NodeEditorWindow.RepaintAll();
            return node;
        }

        /// <summary> 在图中创建原节点的副本 </summary>
        public virtual XNode.Node CopyNode(XNode.Node original) {
            Undo.RecordObject(target, "Duplicate Node");
            XNode.Node node = target.CopyNode(original);
            Undo.RegisterCreatedObjectUndo(node, "Duplicate Node");
            node.name = original.name;
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target))) AssetDatabase.AddObjectToAsset(node, target);
            if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
            return node;
        }

        /// <summary> 对不可移除的节点返回 false </summary>
        public virtual bool CanRemove(XNode.Node node) {
            //检查图上的特性，判断该节点是否为必需节点
            Type graphType = target.GetType();
            XNode.NodeGraph.RequireNodeAttribute[] attribs = Array.ConvertAll(
                graphType.GetCustomAttributes(typeof(XNode.NodeGraph.RequireNodeAttribute), true), x => x as XNode.NodeGraph.RequireNodeAttribute);
            if (attribs.Any(x => x.Requires(node.GetType()))) {
                if (target.nodes.Count(x => x.GetType() == node.GetType()) <= 1) {
                    return false;
                }
            }
            return true;
        }

        /// <summary> 安全移除节点及其所有连接。 </summary>
        public virtual void RemoveNode(XNode.Node node) {
            if (!CanRemove(node)) return;

            //移除节点
            Undo.RecordObject(node, "Delete Node");
            Undo.RecordObject(target, "Delete Node");
            foreach (var port in node.Ports)
                foreach (var conn in port.GetConnections())
                    Undo.RecordObject(conn.node, "Delete Node");
            target.RemoveNode(node);
            Undo.DestroyObjectImmediate(node);
            if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
        }

        [AttributeUsage(AttributeTargets.Class)]
        public class CustomNodeGraphEditorAttribute : Attribute,
        XNodeEditor.Internal.NodeEditorBase<NodeGraphEditor, NodeGraphEditor.CustomNodeGraphEditorAttribute, XNode.NodeGraph>.INodeEditorAttrib {
            private Type inspectedType;
            public string editorPrefsKey;
            /// <summary> 指明该 NodeGraphEditor 是哪种图类型的编辑器 </summary>
            /// <param name="inspectedType">此编辑器可编辑的类型</param>
            /// <param name="editorPrefsKey">为独立的布局设置实例定义唯一键</param>
            public CustomNodeGraphEditorAttribute(Type inspectedType, string editorPrefsKey = "xNode.Settings") {
                this.inspectedType = inspectedType;
                this.editorPrefsKey = editorPrefsKey;
            }

            /// <summary> 返回本特性声明的图类型；NodeEditorBase 靠它建立 图类型 -> 编辑器类型 映射 </summary>
            public Type GetInspectedType() {
                return inspectedType;
            }
        }
    }
}