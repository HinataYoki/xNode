using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using System;
using Object = UnityEngine.Object;

namespace XNodeEditor {
    [InitializeOnLoad]
    public partial class NodeEditorWindow : EditorWindow {
        public static NodeEditorWindow current;

        /// <summary> 缓存所有端口的连线锚点位置 </summary>
        public Dictionary<XNode.NodePort, Rect> portConnectionPoints { get { return _portConnectionPoints; } }
        private Dictionary<XNode.NodePort, Rect> _portConnectionPoints = new Dictionary<XNode.NodePort, Rect>();
        [SerializeField] private NodePortReference[] _references = new NodePortReference[0];
        [SerializeField] private Rect[] _rects = new Rect[0];

        private Func<bool> isDocked {
            get {
                if (_isDocked == null) _isDocked = this.GetIsDockedDelegate();
                return _isDocked;
            }
        }
        private Func<bool> _isDocked;

        /// <summary> 端口的可序列化快照引用（节点 + 字段名），用于窗口重载后恢复锚点缓存 </summary>
        [System.Serializable] private class NodePortReference {
            [SerializeField] private XNode.Node _node;
            [SerializeField] private string _name;

            /// <summary> 从端口创建窗口序列化用的快照引用（只存节点 + 字段名，不直接序列化 NodePort） </summary>
            public NodePortReference(XNode.NodePort nodePort) {
                _node = nodePort.node;
                _name = nodePort.fieldName;
            }

            /// <summary> 按快照重新解析出端口；节点已失效时返回 null </summary>
            public XNode.NodePort GetNodePort() {
                if (_node == null) {
                    return null;
                }
                return _node.GetPort(_name);
            }
        }

        /// <summary>
        /// 窗口禁用/销毁回调：把端口锚点字典压平进两个平行可序列化数组。
        /// EditorWindow 字段会随窗口布局持久化，重载后由 <see cref="OnEnable"/> 恢复。
        /// </summary>
        private void OnDisable() {
            // Cache portConnectionPoints before serialization starts
            int count = portConnectionPoints.Count;
            _references = new NodePortReference[count];
            _rects = new Rect[count];
            int index = 0;
            foreach (var portConnectionPoint in portConnectionPoints) {
                _references[index] = new NodePortReference(portConnectionPoint.Key);
                _rects[index] = portConnectionPoint.Value;
                index++;
            }
        }

        /// <summary> 窗口启用回调：按 OnDisable 缓存的平行数组恢复端口锚点字典（两侧长度一致才恢复） </summary>
        private void OnEnable() {
            // Reload portConnectionPoints if there are any
            int length = _references.Length;
            if (length == _rects.Length) {
                for (int i = 0; i < length; i++) {
                    XNode.NodePort nodePort = _references[i].GetNodePort();
                    if (nodePort != null)
                        _portConnectionPoints.Add(nodePort, _rects[i]);
                }
            }
        }

        public Dictionary<XNode.Node, Vector2> nodeSizes { get { return _nodeSizes; } }
        private Dictionary<XNode.Node, Vector2> _nodeSizes = new Dictionary<XNode.Node, Vector2>();
        public XNode.NodeGraph graph;
        /// <summary> 画布平移偏移；赋值即触发窗口重绘 </summary>
        public Vector2 panOffset { get { return _panOffset; } set { _panOffset = value; Repaint(); } }
        private Vector2 _panOffset;
        /// <summary> 画布缩放倍数，被限制在偏好设置的 min/max 之间；赋值即触发重绘 </summary>
        public float zoom { get { return _zoom; } set { _zoom = Mathf.Clamp(value, NodeEditorPreferences.GetSettings().minZoom, NodeEditorPreferences.GetSettings().maxZoom); Repaint(); } }
        private float _zoom = 1;

        void OnFocus() {
            current = this;
            ValidateGraphEditor();
            if (graphEditor != null) {
                graphEditor.OnWindowFocus();
                if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
            }

            dragThreshold = Math.Max(1f, Screen.width / 1000f);
        }
        
        void OnLostFocus() {
            if (graphEditor != null) graphEditor.OnWindowFocusLost();
        }

        [InitializeOnLoadMethod]
        private static void OnLoad() {
            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;
        }

        /// <summary> 处理 Project 窗口选中变化：新建的场景内图自动打开编辑器 </summary>
        private static void OnSelectionChanged() {
            XNode.NodeGraph nodeGraph = Selection.activeObject as XNode.NodeGraph;
            if (nodeGraph && !AssetDatabase.Contains(nodeGraph)) {
                if (NodeEditorPreferences.GetSettings().openOnCreate) Open(nodeGraph);
            }
        }

        /// <summary> 确认图编辑器已创建且指向当前图 </summary>
        private void ValidateGraphEditor() {
            NodeGraphEditor graphEditor = NodeGraphEditor.GetEditor(graph, this);
            if (this.graphEditor != graphEditor && graphEditor != null) {
                this.graphEditor = graphEditor;
                graphEditor.OnOpen();
            }
        }

        /// <summary> 创建编辑器窗口 </summary>
        public static NodeEditorWindow Init() {
            NodeEditorWindow w = CreateInstance<NodeEditorWindow>();
            w.titleContent = new GUIContent("xNode");
            w.wantsMouseMove = true;
            w.Show();
            return w;
        }

        /// <summary> 图在资产内则直接保存，否则弹出另存为 </summary>
        public void Save() {
            if (AssetDatabase.Contains(graph)) {
                EditorUtility.SetDirty(graph);
                if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
            } else SaveAs();
        }

        /// <summary>
        /// 另存为：弹出项目内保存面板，把当前图写到指定路径并挂为主资产；
        /// 目标路径已有同名图时先删除旧资产。
        /// </summary>
        public void SaveAs() {
            string path = EditorUtility.SaveFilePanelInProject("Save NodeGraph", "NewNodeGraph", "asset", "");
            if (string.IsNullOrEmpty(path)) return;
            else {
                XNode.NodeGraph existingGraph = AssetDatabase.LoadAssetAtPath<XNode.NodeGraph>(path);
                if (existingGraph != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(graph, path);
                EditorUtility.SetDirty(graph);
                if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
            }
        }

        /// <summary> GUI.Window 绘制回调，仅提供标题栏拖动，无窗口内容 </summary>
        private void DraggableWindow(int windowID) {
            GUI.DragWindow();
        }

        /// <summary> 窗口坐标转画布网格坐标 </summary>
        public Vector2 WindowToGridPosition(Vector2 windowPosition) {
            return (windowPosition - (position.size * 0.5f) - (panOffset / zoom)) * zoom;
        }

        /// <summary> 画布网格坐标转窗口坐标 </summary>
        public Vector2 GridToWindowPosition(Vector2 gridPosition) {
            return (position.size * 0.5f) + (panOffset / zoom) + (gridPosition / zoom);
        }

        /// <summary> 网格矩形转窗口矩形：位置走像素对齐换算，尺寸不变 </summary>
        public Rect GridToWindowRectNoClipped(Rect gridRect) {
            gridRect.position = GridToWindowPositionNoClipped(gridRect.position);
            return gridRect;
        }

        /// <summary> 网格矩形转窗口矩形：位置走常规换算，尺寸除以缩放倍数 </summary>
        public Rect GridToWindowRect(Rect gridRect) {
            gridRect.position = GridToWindowPosition(gridRect.position);
            gridRect.size /= zoom;
            return gridRect;
        }

        /// <summary> 网格坐标转窗口坐标并对齐到整数像素，保证 UI 锐利（对最终偏移取整而非 panOffset） </summary>
        public Vector2 GridToWindowPositionNoClipped(Vector2 gridPosition) {
            Vector2 center = position.size * 0.5f;
            float xOffset = Mathf.Round(center.x * zoom + (panOffset.x + gridPosition.x));
            float yOffset = Mathf.Round(center.y * zoom + (panOffset.y + gridPosition.y));
            return new Vector2(xOffset, yOffset);
        }

        /// <summary> 选中节点；add 为 true 时追加进当前多选 </summary>
        public void SelectNode(XNode.Node node, bool add) {
            if (add) {
                List<Object> selection = new List<Object>(Selection.objects);
                selection.Add(node);
                Selection.objects = selection.ToArray();
            } else Selection.objects = new Object[] { node };
        }

        /// <summary> 从多选中移除节点 </summary>
        public void DeselectNode(XNode.Node node) {
            List<Object> selection = new List<Object>(Selection.objects);
            selection.Remove(node);
            Selection.objects = selection.ToArray();
        }

        [OnOpenAsset(0)]
        public static bool OnOpen(int instanceID, int line) {
            XNode.NodeGraph nodeGraph = EditorUtility.InstanceIDToObject(instanceID) as XNode.NodeGraph;
            if (nodeGraph != null) {
                Open(nodeGraph);
                return true;
            }
            return false;
        }

        /// <summary> 在节点编辑器中打开指定图 </summary>
        public static NodeEditorWindow Open(XNode.NodeGraph graph) {
            if (!graph) return null;

            NodeEditorWindow w = GetWindow(typeof(NodeEditorWindow), false, "xNode", true) as NodeEditorWindow;
            w.wantsMouseMove = true;
            w.graph = graph;
            return w;
        }

        /// <summary> 重绘所有打开的节点编辑器窗口；顺带清理注册表里已关闭的窗口 </summary>
        public static void RepaintAll() {
            NodeEditorWindow[] windows = Resources.FindObjectsOfTypeAll<NodeEditorWindow>();
            for (int i = 0; i < windows.Length; i++) {
                windows[i].Repaint();
            }
        }
    }
}
