using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
#endif
#if UNITY_2019_1_OR_NEWER && USE_ADVANCED_GENERIC_MENU
using GenericMenu = XNodeEditor.AdvancedGenericMenu;
#endif

namespace XNodeEditor {
    /// <summary> 自定义节点编辑器的基类。继承它可为节点创建自定义的检视器与编辑器。 </summary>
    [CustomNodeEditor(typeof(XNode.Node))]
    public class NodeEditor : XNodeEditor.Internal.NodeEditorBase<NodeEditor, NodeEditor.CustomNodeEditorAttribute, XNode.Node> {

        /// <summary> 节点在编辑器中被修改时触发 </summary>
        public static Action<XNode.Node> onUpdateNode;

        // OnBodyGUI 跳过的序列化属性名；提为静态避免每帧每节点分配数组
        private static readonly string[] s_bodyExcludes = { "m_Script", "graph", "position", "ports" };
        /// <summary>
        /// 兼容门面：转发到最近聚焦窗口（<see cref="NodeEditorWindow.current"/>）的端口手柄位置表，
        /// 避免多窗口共享一份导致锚点串窗。无窗口时返回内部空表，只读安全。
        /// </summary>
        public static Dictionary<XNode.NodePort, Vector2> portPositions {
            get { return NodeEditorWindow.current != null ? NodeEditorWindow.current.portPositions : _orphanPortPositions; }
        }
        private static readonly Dictionary<XNode.NodePort, Vector2> _orphanPortPositions = new Dictionary<XNode.NodePort, Vector2>();

#if ODIN_INSPECTOR
        /// <summary> Odin 绘制期间的递归守卫：置位时 Odin 特性处理器按节点编辑器环境工作 </summary>
        protected internal static bool inNodeEditor = false;
#endif

        /// <summary> 绘制节点标题栏：默认画节点名（居中加粗白字，高 30） </summary>
        public virtual void OnHeaderGUI() {
            GUILayout.Label(target.name, NodeEditorResources.styles.nodeHeader, GUILayout.Height(30));
        }

        /// <summary> 为所有公共字段绘制标准字段编辑器 </summary>
        public virtual void OnBodyGUI() {
#if ODIN_INSPECTOR
            inNodeEditor = true;
#endif

            // Unity 明确要求这样才能保存/更新任何序列化对象。
            // serializedObject.Update(); 必须放在检视器 GUI 的开头，
            // serializedObject.ApplyModifiedProperties(); 则放在结尾。
            serializedObject.Update();

#if ODIN_INSPECTOR
            try
            {
#if ODIN_INSPECTOR_3
                objectTree.BeginDraw( true );
#else
                InspectorUtilities.BeginDrawPropertyTree(objectTree, true);
#endif
            }
            catch ( ArgumentNullException )
            {
#if ODIN_INSPECTOR_3
                objectTree.EndDraw();
#else
                InspectorUtilities.EndDrawPropertyTree(objectTree);
#endif
                NodeEditor.DestroyEditor(this.target);
                return;
            }

            GUIHelper.PushLabelWidth( 84 );
            objectTree.Draw( true );
#if ODIN_INSPECTOR_3
            objectTree.EndDraw();
#else
            InspectorUtilities.EndDrawPropertyTree(objectTree);
#endif
            GUIHelper.PopLabelWidth();
#else

            //遍历序列化属性并像 Inspector 一样绘制（但带端口）
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren)) {
                enterChildren = false;
                if (s_bodyExcludes.Contains(iterator.name)) continue;
                NodeEditorGUILayout.PropertyField(iterator, true);
            }
#endif

            //按序列化顺序遍历并绘制动态端口
            foreach (XNode.NodePort dynamicPort in target.DynamicPorts) {
                if (NodeEditorGUILayout.IsDynamicPortListPort(dynamicPort)) continue;
                NodeEditorGUILayout.PortField(dynamicPort);
            }

            serializedObject.ApplyModifiedProperties();

#if ODIN_INSPECTOR
            //调用重绘，使图窗口元素正确响应来自 Odin 的布局变化
            if (GUIHelper.RepaintRequested) {
                GUIHelper.ClearRepaintRequest();
                window.Repaint();
            }
#endif

#if ODIN_INSPECTOR
            inNodeEditor = false;
#endif
        }

        /// <summary> 节点宽度：[NodeWidth] 特性优先，未标注时默认 208 像素 </summary>
        public virtual int GetWidth() {
            Type type = target.GetType();
            int width;
            if (type.TryGetAttributeWidth(out width)) return width;
            else return 208;
        }

        /// <summary> 返回目标节点的颜色 </summary>
        public virtual Color GetTint() {
            //尝试从 [NodeTint] 特性获取颜色
            Type type = target.GetType();
            Color color;
            if (type.TryGetAttributeTint(out color)) return color;
            //返回默认颜色（灰色）
            else return NodeEditorPreferences.GetSettings().tintColor;
        }

        /// <summary> 节点主体样式：圆角九宫格背景贴图，DrawNodes 据此包出节点面板 </summary>
        public virtual GUIStyle GetBodyStyle() {
            return NodeEditorResources.styles.nodeBody;
        }

        /// <summary> 选中态的高亮描边样式；DrawNodes 会把它叠在主体样式外层 </summary>
        public virtual GUIStyle GetBodyHighlightStyle() {
            return NodeEditorResources.styles.nodeHighlight;
        }

        // 选中态样式拷贝缓存：DrawNodes 需要交换主体/高亮两层的 padding，
        // 每帧 new GUIStyle 拷贝会产生 GC，这里拷一次复用，底层纹理变化（换皮肤）时重建
        private GUIStyle _bodyStyleCopy;
        private GUIStyle _highlightStyleCopy;
        private static readonly RectOffset _emptyPadding = new RectOffset();

        /// <summary> 取选中节点使用的样式对：body 已清空内边距，highlight 带原始内边距作描边层 </summary>
        internal void GetBodyStylesForSelection(out GUIStyle body, out GUIStyle highlight) {
            GUIStyle src = GetBodyStyle();
            if (_bodyStyleCopy == null || _bodyStyleCopy.normal.background != src.normal.background) {
                _bodyStyleCopy = new GUIStyle(src);
                _highlightStyleCopy = new GUIStyle(GetBodyHighlightStyle());
                _highlightStyleCopy.padding = _bodyStyleCopy.padding;
                _bodyStyleCopy.padding = _emptyPadding;
            }
            body = _bodyStyleCopy;
            highlight = _highlightStyleCopy;
        }

        /// <summary> 重写此方法以显示自定义的节点头部工具提示 </summary>
        public virtual string GetHeaderTooltip() {
            return null;
        }

        /// <summary> 右键点击节点时向上下文菜单添加菜单项。可重写此方法以添加自定义菜单项。 </summary>
        public virtual void AddContextMenuItems(GenericMenu menu) {
            bool canRemove = true;
            //仅选中单个节点时可用的操作
            if (Selection.objects.Length == 1 && Selection.activeObject is XNode.Node) {
                XNode.Node node = Selection.activeObject as XNode.Node;
                menu.AddItem(new GUIContent("Move To Top"), false, () => NodeEditorWindow.current.MoveNodeToTop(node));
                menu.AddItem(new GUIContent("Rename"), false, NodeEditorWindow.current.RenameSelectedNode);

                canRemove = NodeGraphEditor.GetEditor(node.graph, NodeEditorWindow.current).CanRemove(node);
            }

            //对任意数量选中节点都可用的操作
            menu.AddItem(new GUIContent("Copy"), false, NodeEditorWindow.current.CopySelectedNodes);
            menu.AddItem(new GUIContent("Duplicate"), false, NodeEditorWindow.current.DuplicateSelectedNodes);

            if (canRemove) menu.AddItem(new GUIContent("Remove"), false, NodeEditorWindow.current.RemoveSelectedNodes);
            else menu.AddItem(new GUIContent("Remove"), false, null);

            //仅选中单个节点时的自定义操作
            if (Selection.objects.Length == 1 && Selection.activeObject is XNode.Node) {
                XNode.Node node = Selection.activeObject as XNode.Node;
                menu.AddCustomContextMenuItems(node);
            }
        }

        /// <summary> 重命名节点资源。这会触发该节点重新导入。 </summary>
        public void Rename(string newName) {
            if (newName == null || newName.Trim() == "") newName = NodeEditorUtilities.NodeDefaultName(target.GetType());
            target.name = newName;
            OnRename();
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(target));
        }

        /// <summary> 节点名称更改后调用。 </summary>
        public virtual void OnRename() { }

        [AttributeUsage(AttributeTargets.Class)]
        public class CustomNodeEditorAttribute : Attribute,
        XNodeEditor.Internal.NodeEditorBase<NodeEditor, NodeEditor.CustomNodeEditorAttribute, XNode.Node>.INodeEditorAttrib {
            private Type inspectedType;
            /// <summary> 指明该 NodeEditor 是哪种节点类型的编辑器 </summary>
            /// <param name="inspectedType">此编辑器可编辑的类型</param>
            public CustomNodeEditorAttribute(Type inspectedType) {
                this.inspectedType = inspectedType;
            }

            /// <summary> 返回本特性声明的节点类型；NodeEditorBase 靠它建立 目标类型 -> 编辑器类型 映射 </summary>
            public Type GetInspectedType() {
                return inspectedType;
            }
        }
    }
}