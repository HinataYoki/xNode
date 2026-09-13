using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XNodeEditor.Internal;
#if UNITY_2019_1_OR_NEWER && USE_ADVANCED_GENERIC_MENU
using GenericMenu = XNodeEditor.AdvancedGenericMenu;
#endif

namespace XNodeEditor {
    public partial class NodeEditorWindow {
        public enum NodeActivity { Idle, HoldNode, DragNode, HoldGrid, DragGrid }

        public static XNode.Node[] copyBuffer = null;

        /// <summary>
        /// 兼容门面：转发到最近聚焦窗口（<see cref="current"/>）的交互状态。
        /// 交互状态本体已按窗口实例隔离，多窗口互不干扰；current 为 null 时读返回默认值、写被忽略。
        /// </summary>
        public static NodeActivity currentActivity {
            get { return current != null ? current._activity : NodeActivity.Idle; }
            set { if (current != null) current._activity = value; }
        }
        /// <summary> 兼容门面：语义同 <see cref="currentActivity"/> </summary>
        public static bool isPanning {
            get { return current != null ? current._isPanning : false; }
            private set { if (current != null) current._isPanning = value; }
        }
        /// <summary> 兼容门面：语义同 <see cref="currentActivity"/> </summary>
        public static Vector2[] dragOffset {
            get { return current != null ? current._dragOffset : null; }
            set { if (current != null) current._dragOffset = value; }
        }

        /// <summary> 本窗口当前交互阶段（拖节点/拖框选/平移等） </summary>
        [NonSerialized] private NodeActivity _activity = NodeActivity.Idle;
        /// <summary> 本窗口是否正在按住右/中键平移画布 </summary>
        [NonSerialized] private bool _isPanning;
        /// <summary> 拖动开始时记录的各选中节点/重路由点相对鼠标的偏移表 </summary>
        [NonSerialized] private Vector2[] _dragOffset;
        /// <summary> 端口枚举复用缓冲，替代拖动路径的 yield 迭代器 </summary>
        [NonSerialized] private readonly List<XNode.NodePort> _portBuffer = new List<XNode.NodePort>(16);

        public bool IsDraggingPort { get { return draggedOutput != null; } }
        public bool IsHoveringPort { get { return hoveredPort != null; } }
        public bool IsHoveringNode { get { return hoveredNode != null; } }
        public bool IsHoveringReroute { get { return hoveredReroute.port != null; } }

        /// <summary> 拖拽中的输出端口；未拖线时为 null </summary>
        public XNode.NodePort DraggedOutputPort { get { return draggedOutput; } }
        /// <summary> 当前悬停的端口 </summary>
        public XNode.NodePort HoveredPort { get { return hoveredPort; } }
        /// <summary> 当前悬停的节点 </summary>
        public XNode.Node HoveredNode { get { return hoveredNode; } }

        private XNode.Node hoveredNode = null;
        [NonSerialized] public XNode.NodePort hoveredPort = null;
        [NonSerialized] private XNode.NodePort draggedOutput = null;
        [NonSerialized] private XNode.NodePort draggedOutputTarget = null;
        [NonSerialized] private XNode.NodePort autoConnectOutput = null;
        [NonSerialized] private List<Vector2> draggedOutputReroutes = new List<Vector2>();

        /// <summary> 当前鼠标悬停的重路由点引用；每帧在 DrawConnections 中重算 </summary>
        private RerouteReference hoveredReroute = new RerouteReference();
        [NonSerialized] public List<RerouteReference> selectedReroutes = new List<RerouteReference>();
        private Vector2 dragBoxStart;
        private UnityEngine.Object[] preBoxSelection;
        private RerouteReference[] preBoxSelectionReroute;
        private Rect selectionBox;
        private bool isDoubleClick = false;
        private Vector2 lastMousePosition;
        private float dragThreshold = 1f;

        /// <summary> 画布交互总入口：处理拖拽文件、滚轮缩放、拖动节点/平移画布、选择、右键菜单与快捷键命令 </summary>
        public void Controls() {
            wantsMouseMove = true;
            Event e = Event.current;
            switch (e.type) {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
                    if (e.type == EventType.DragPerform) {
                        DragAndDrop.AcceptDrag();
                        graphEditor.OnDropObjects(DragAndDrop.objectReferences);
                    }
                    break;
                case EventType.MouseMove:
                    // 键盘命令无法从 Event 拿到正确的鼠标位置，用 MouseMove 记录
                    lastMousePosition = e.mousePosition;
                    break;
                case EventType.ScrollWheel:
                    float oldZoom = zoom;
                    if (e.delta.y > 0) zoom += 0.1f * zoom;
                    else zoom -= 0.1f * zoom;
                    // 缩放锚定到鼠标位置（可在偏好设置关闭）
                    if (NodeEditorPreferences.GetSettings().zoomToMouse) panOffset += (1 - oldZoom / zoom) * (WindowToGridPosition(e.mousePosition) + panOffset);
                    break;
                case EventType.MouseDrag:
                    if (e.button == 0) {
                        if (IsDraggingPort) {
                            // 悬停在合法输入端口上就记录目标（即使不能连接也记录，避免误弹自动连接菜单）
                            if (IsHoveringPort && hoveredPort.IsInput && !draggedOutput.IsConnectedTo(hoveredPort)) {
                                draggedOutputTarget = hoveredPort;
                            } else {
                                draggedOutputTarget = null;
                            }
                            Repaint();
                        } else if (_activity == NodeActivity.HoldNode) {
                            RecalculateDragOffsets(e);
                            _activity = NodeActivity.DragNode;
                            Repaint();
                        }
                        if (_activity == NodeActivity.DragNode) {
                            // 按住 Ctrl 反转网格吸附开关
                            bool gridSnap = NodeEditorPreferences.GetSettings().gridSnap;
                            if (e.control) gridSnap = !gridSnap;

                            Vector2 mousePos = WindowToGridPosition(e.mousePosition);
                            // 按偏移量移动选中节点
                            for (int i = 0; i < Selection.objects.Length; i++) {
                                if (Selection.objects[i] is XNode.Node) {
                                    XNode.Node node = Selection.objects[i] as XNode.Node;
                                    Undo.RecordObject(node, "Moved Node");
                                    Vector2 initial = node.position;
                                    node.position = mousePos + _dragOffset[i];
                                    if (gridSnap) {
                                        node.position.x = (Mathf.Round((node.position.x + 8) / 16) * 16) - 8;
                                        node.position.y = (Mathf.Round((node.position.y + 8) / 16) * 16) - 8;
                                    }

                                    // 节点被拖动时立即平移端口锚点缓存，避免连线延迟一帧才跟随
                                    Vector2 offset = node.position - initial;
                                    if (offset.sqrMagnitude > 0) {
                                        node.GetPorts(_portBuffer);
                                        for (int p = 0; p < _portBuffer.Count; p++) {
                                            Rect rect;
                                            if (portConnectionPoints.TryGetValue(_portBuffer[p], out rect)) {
                                                rect.position += offset;
                                                portConnectionPoints[_portBuffer[p]] = rect;
                                            }
                                        }
                                    }
                                }
                            }
                            // 按偏移量移动选中的重路由点
                            for (int i = 0; i < selectedReroutes.Count; i++) {
                                Vector2 pos = mousePos + _dragOffset[Selection.objects.Length + i];
                                if (gridSnap) {
                                    pos.x = (Mathf.Round(pos.x / 16) * 16);
                                    pos.y = (Mathf.Round(pos.y / 16) * 16);
                                }
                                selectedReroutes[i].SetPoint(pos);
                            }
                            Repaint();
                        } else if (_activity == NodeActivity.HoldGrid) {
                            _activity = NodeActivity.DragGrid;
                            preBoxSelection = Selection.objects;
                            preBoxSelectionReroute = selectedReroutes.ToArray();
                            dragBoxStart = WindowToGridPosition(e.mousePosition);
                            Repaint();
                        } else if (_activity == NodeActivity.DragGrid) {
                            // 框选：归一化负尺寸后记录选择框
                            Vector2 boxStartPos = GridToWindowPosition(dragBoxStart);
                            Vector2 boxSize = e.mousePosition - boxStartPos;
                            if (boxSize.x < 0) { boxStartPos.x += boxSize.x; boxSize.x = Mathf.Abs(boxSize.x); }
                            if (boxSize.y < 0) { boxStartPos.y += boxSize.y; boxSize.y = Mathf.Abs(boxSize.y); }
                            selectionBox = new Rect(boxStartPos, boxSize);
                            Repaint();
                        }
                    } else if (e.button == 1 || e.button == 2) {
                        // 大屏幕下拖动阈值校验，避免点击误判为平移
                        if (e.delta.magnitude > dragThreshold) {
                            panOffset += e.delta * zoom;
                            _isPanning = true;
                        }
                    }
                    break;
                case EventType.MouseDown:
                    Repaint();
                    if (e.button == 0) {
                        draggedOutputReroutes.Clear();

                        if (IsHoveringPort) {
                            if (hoveredPort.IsOutput) {
                                // 按住输出端口：开始拖线
                                draggedOutput = hoveredPort;
                                autoConnectOutput = hoveredPort;
                            } else {
                                // 按住已连接的输入端口：把现有连线拽出来继续拖
                                hoveredPort.VerifyConnections();
                                autoConnectOutput = null;
                                if (hoveredPort.IsConnected) {
                                    XNode.Node node = hoveredPort.node;
                                    XNode.NodePort output = hoveredPort.Connection;
                                    int outputConnectionIndex = output.GetConnectionIndex(hoveredPort);
                                    draggedOutputReroutes = output.GetReroutePoints(outputConnectionIndex);
                                    hoveredPort.Disconnect(output);
                                    draggedOutput = output;
                                    draggedOutputTarget = hoveredPort;
                                    if (NodeEditor.onUpdateNode != null) NodeEditor.onUpdateNode(node);
                                }
                            }
                        } else if (IsHoveringNode && IsHoveringTitle(hoveredNode)) {
                            // 按在节点标题栏上：处理选择/反选，双击状态记录到 MouseUp 再生效
                            if (!Selection.Contains(hoveredNode)) {
                                SelectNode(hoveredNode, e.control || e.shift);
                                if (!e.control && !e.shift) selectedReroutes.Clear();
                            } else if (e.control || e.shift) DeselectNode(hoveredNode);

                            // clickCount 只在 MouseDown 有效，双击动作放到 MouseUp 处理
                            isDoubleClick = (e.clickCount == 2);

                            e.Use();
                            _activity = NodeActivity.HoldNode;
                        } else if (IsHoveringReroute) {
                            // 按在重路由点上：处理选择/反选
                            if (!selectedReroutes.Contains(hoveredReroute)) {
                                if (e.control || e.shift) selectedReroutes.Add(hoveredReroute);
                                else {
                                    selectedReroutes = new List<RerouteReference>() { hoveredReroute };
                                    Selection.activeObject = null;
                                }

                            }
                            else if (e.control || e.shift) selectedReroutes.Remove(hoveredReroute);
                            for (int i = 0; i < selectedReroutes.Count; i++) {
                                if (selectedReroutes[i].port != null && selectedReroutes[i].port.node != null)
                                    Undo.RecordObject(selectedReroutes[i].port.node, "Move Reroute Point");
                            }
                            e.Use();
                            _activity = NodeActivity.HoldNode;
                        }
                        // 按在画布空白处：开始框选或清除选择
                        else if (!IsHoveringNode) {
                            _activity = NodeActivity.HoldGrid;
                            if (!e.control && !e.shift) {
                                selectedReroutes.Clear();
                                Selection.activeObject = null;
                            }
                        }
                    }
                    break;
                case EventType.MouseUp:
                    if (e.button == 0) {
                        // 拖线释放
                        if (IsDraggingPort) {
                            // 目标合法则建立连接，并把拖线途中加的重路由点挂到新连线上
                            if (draggedOutputTarget != null && graphEditor.CanConnect(draggedOutput, draggedOutputTarget)) {
                                XNode.Node node = draggedOutputTarget.node;
                                if (graph.nodes.Count != 0) draggedOutput.Connect(draggedOutputTarget);

                                // 连接可能刚建立就被移除，此时索引为 -1
                                int connectionIndex = draggedOutput.GetConnectionIndex(draggedOutputTarget);
                                if (connectionIndex != -1) {
                                    draggedOutput.GetReroutePoints(connectionIndex).AddRange(draggedOutputReroutes);
                                    if (NodeEditor.onUpdateNode != null) NodeEditor.onUpdateNode(node);
                                    EditorUtility.SetDirty(graph);
                                }
                            }
                            // 拖到空白处：按偏好设置弹出"拖线创建节点"菜单
                            else if (draggedOutputTarget == null && NodeEditorPreferences.GetSettings().dragToCreate && autoConnectOutput != null) {
                                GenericMenu menu = new GenericMenu();
                                graphEditor.AddContextMenuItems(menu, draggedOutput);
                                menu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
                            }
                            draggedOutput = null;
                            draggedOutputTarget = null;
                            EditorUtility.SetDirty(graph);
                            if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
                        } else if (_activity == NodeActivity.DragNode) {
                            // 拖动节点结束：标脏并按需自动保存
                            for (int i = 0; i < Selection.objects.Length; i++) {
                                if (Selection.objects[i] is XNode.Node) EditorUtility.SetDirty(Selection.objects[i]);
                            }
                            for (int i = 0; i < selectedReroutes.Count; i++) {
                                if (selectedReroutes[i].port != null && selectedReroutes[i].port.node != null)
                                    EditorUtility.SetDirty(selectedReroutes[i].port.node);
                            }
                            if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
                        } else if (!IsHoveringNode) {
                            // 点击画布空白处：释放字段焦点
                            if (!_isPanning) {
                                EditorGUI.FocusTextInControl(null);
                                EditorGUIUtility.editingTextField = false;
                            }
                            if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
                        }

                        // 点击节点标题栏：选中它；双击则居中该节点
                        if (_activity == NodeActivity.HoldNode && !(e.control || e.shift)) {
                            selectedReroutes.Clear();
                            SelectNode(hoveredNode, false);

                            if (isDoubleClick) {
                                Vector2 size;
                                nodeSizes.TryGetValue(hoveredNode, out size);
                                panOffset = -hoveredNode.position - size / 2;
                            }
                        }

                        // 点击重路由点：选中它
                        if (IsHoveringReroute && !(e.control || e.shift)) {
                            selectedReroutes = new List<RerouteReference>() { hoveredReroute };
                            Selection.activeObject = null;
                        }

                        Repaint();
                        _activity = NodeActivity.Idle;
                    } else if (e.button == 1 || e.button == 2) {
                        // 右键/中键：没在平移时按悬停对象弹出对应上下文菜单
                        if (!_isPanning) {
                            if (IsDraggingPort) {
                                // 拖线中右键：在鼠标处添加重路由点
                                draggedOutputReroutes.Add(WindowToGridPosition(e.mousePosition));
                            } else if (_activity == NodeActivity.DragNode && Selection.activeObject == null && selectedReroutes.Count == 1) {
                                // 拖动单个重路由点时右键：在当前位置后插入新点
                                selectedReroutes[0].InsertPoint(selectedReroutes[0].GetPoint());
                                if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
                                selectedReroutes[0] = new RerouteReference(selectedReroutes[0].port, selectedReroutes[0].connectionIndex, selectedReroutes[0].pointIndex + 1);
                            } else if (IsHoveringReroute) {
                                ShowRerouteContextMenu(hoveredReroute);
                            } else if (IsHoveringPort) {
                                ShowPortContextMenu(hoveredPort);
                            } else if (IsHoveringNode && IsHoveringTitle(hoveredNode)) {
                                if (!Selection.Contains(hoveredNode)) SelectNode(hoveredNode, false);
                                autoConnectOutput = null;
                                GenericMenu menu = new GenericMenu();
                                NodeEditor.GetEditor(hoveredNode, this).AddContextMenuItems(menu);
                                menu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
                                e.Use(); // 修复 Unity 5.6.6f2 下复制/粘贴菜单弹出的问题（2018.3.2f1 已无此问题，其他位置可能也需要）
                            } else if (!IsHoveringNode) {
                                autoConnectOutput = null;
                                GenericMenu menu = new GenericMenu();
                                graphEditor.AddContextMenuItems(menu);
                                menu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
                            }
                        }
                        _isPanning = false;
                    }
                    // 重置双击状态
                    isDoubleClick = false;
                    break;
                case EventType.KeyDown:
                    if (EditorGUIUtility.editingTextField || GUIUtility.keyboardControl != 0) break;
                    // F 键：聚焦选中节点；F2/回车：重命名
                    else if (e.keyCode == KeyCode.F) Home();
                    if (NodeEditorUtilities.IsMac()) {
                        if (e.keyCode == KeyCode.Return) RenameSelectedNode();
                    } else {
                        if (e.keyCode == KeyCode.F2) RenameSelectedNode();
                    }
                    // A 键：全选/取消全选本图节点
                    if (e.keyCode == KeyCode.A) {
                        if (Selection.objects.Length > 0 && HasSelectedNodes()) {
                            for (int i = 0; i < graph.nodes.Count; i++) {
                                DeselectNode(graph.nodes[i]);
                            }
                        } else {
                            for (int i = 0; i < graph.nodes.Count; i++) {
                                SelectNode(graph.nodes[i], true);
                            }
                        }
                        Repaint();
                    }
                    break;
                case EventType.ValidateCommand:
                case EventType.ExecuteCommand:
                    if (e.commandName == "SoftDelete") {
                        if (e.type == EventType.ExecuteCommand) RemoveSelectedNodes();
                        e.Use();
                    } else if (NodeEditorUtilities.IsMac() && e.commandName == "Delete") {
                        if (e.type == EventType.ExecuteCommand) RemoveSelectedNodes();
                        e.Use();
                    } else if (e.commandName == "Duplicate") {
                        if (e.type == EventType.ExecuteCommand) DuplicateSelectedNodes();
                        e.Use();
                    } else if (e.commandName == "Copy") {
                        if (!EditorGUIUtility.editingTextField) {
                            if (e.type == EventType.ExecuteCommand) CopySelectedNodes();
                            e.Use();
                        }
                    } else if (e.commandName == "Paste") {
                        if (!EditorGUIUtility.editingTextField) {
                            if (e.type == EventType.ExecuteCommand) PasteNodes(WindowToGridPosition(lastMousePosition));
                            e.Use();
                        }
                    }
                    Repaint();
                    break;
                case EventType.Ignore:
                    // 鼠标在窗口外释放时结束框选
                    if (e.rawType == EventType.MouseUp && _activity == NodeActivity.DragGrid) {
                        Repaint();
                        _activity = NodeActivity.Idle;
                    }
                    break;
            }
        }

        /// <summary> 当前 Selection 是否包含本图节点（替代 LINQ，OnGUI 路径零分配） </summary>
        private bool HasSelectedNodes() {
            UnityEngine.Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++) {
                if (selection[i] is XNode.Node && graph.nodes.Contains(selection[i] as XNode.Node)) return true;
            }
            return false;
        }

        /// <summary> 记录每个选中节点/重路由点相对鼠标的偏移，供拖动时使用 </summary>
        private void RecalculateDragOffsets(Event current) {
            _dragOffset = new Vector2[Selection.objects.Length + selectedReroutes.Count];
            for (int i = 0; i < Selection.objects.Length; i++) {
                if (Selection.objects[i] is XNode.Node) {
                    XNode.Node node = Selection.objects[i] as XNode.Node;
                    _dragOffset[i] = node.position - WindowToGridPosition(current.mousePosition);
                }
            }

            for (int i = 0; i < selectedReroutes.Count; i++) {
                _dragOffset[Selection.objects.Length + i] = selectedReroutes[i].GetPoint() - WindowToGridPosition(current.mousePosition);
            }
        }

        /// <summary> 把全部选中节点置于视野中心；无选中节点时重置视图与缩放到原点 </summary>
        public void Home() {
            Vector2 minPos = Vector2.zero, maxPos = Vector2.zero;
            bool hasNodes = false;
            UnityEngine.Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++) {
                XNode.Node node = selection[i] as XNode.Node;
                if (node == null) continue;
                Vector2 size;
                if (!nodeSizes.TryGetValue(node, out size)) size = Vector2.zero;
                if (hasNodes) {
                    minPos = Vector2.Min(minPos, node.position);
                    maxPos = Vector2.Max(maxPos, node.position + size);
                } else {
                    minPos = node.position;
                    maxPos = node.position + size;
                    hasNodes = true;
                }
            }
            if (hasNodes) {
                panOffset = -(minPos + (maxPos - minPos) / 2f);
            } else {
                zoom = 2;
                panOffset = Vector2.zero;
            }
        }

        /// <summary> 移除当前选中的节点与重路由点 </summary>
        public void RemoveSelectedNodes() {
            // 从高索引往低索引删，避免删除时索引位移
            for (int i = selectedReroutes.Count - 1; i >= 0; i--) {
                selectedReroutes[i].RemovePoint();
            }
            selectedReroutes.Clear();
            if (graph != null) {
                EditorUtility.SetDirty(graph);
                if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
            }
            UnityEngine.Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++) {
                if (selection[i] is XNode.Node) {
                    graphEditor.RemoveNode(selection[i] as XNode.Node);
                }
            }
        }

        /// <summary> 对当前选中的唯一节点发起重命名弹窗 </summary>
        public void RenameSelectedNode() {
            if (Selection.objects.Length == 1 && Selection.activeObject is XNode.Node) {
                XNode.Node node = Selection.activeObject as XNode.Node;
                Vector2 size;
                if (nodeSizes.TryGetValue(node, out size)) {
                    RenamePopup.Show(Selection.activeObject, size.x);
                } else {
                    RenamePopup.Show(Selection.activeObject);
                }
            }
        }

        /// <summary> 把节点移到 graph.nodes 末尾，使其绘制在其它节点之上 </summary>
        public void MoveNodeToTop(XNode.Node node) {
            if (node == null || graph == null || node.graph != graph || !graph.nodes.Contains(node)) return;
            Undo.RecordObject(graph, "Move Node To Top");
            graph.nodes.Remove(node);
            graph.nodes.Add(node);
            EditorUtility.SetDirty(graph);
            if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
        }

        /// <summary> 复制选中节点并选中新副本 </summary>
        public void DuplicateSelectedNodes() {
            // 只取属于当前图的选中节点
            List<XNode.Node> selectedNodes = new List<XNode.Node>();
            UnityEngine.Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++) {
                XNode.Node node = selection[i] as XNode.Node;
                if (node != null && node.graph == graph) selectedNodes.Add(node);
            }
            if (selectedNodes.Count == 0) return;

            // 以左上角节点为基准整体偏移 30 像素
            Vector2 topLeftNode = selectedNodes[0].position;
            for (int i = 1; i < selectedNodes.Count; i++) {
                topLeftNode = Vector2.Min(topLeftNode, selectedNodes[i].position);
            }
            InsertDuplicateNodes(selectedNodes.ToArray(), topLeftNode + new Vector2(30, 30));
        }

        /// <summary> 把选中节点复制进剪贴缓冲 </summary>
        public void CopySelectedNodes() {
            List<XNode.Node> copied = new List<XNode.Node>();
            UnityEngine.Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++) {
                XNode.Node node = selection[i] as XNode.Node;
                if (node != null && node.graph == graph) copied.Add(node);
            }
            copyBuffer = copied.ToArray();
        }

        /// <summary> 把剪贴缓冲中的节点粘贴到指定位置 </summary>
        public void PasteNodes(Vector2 pos) {
            InsertDuplicateNodes(copyBuffer, pos);
        }

        /// <summary> 复制一组节点到偏移位置并重建内部连接；受 DisallowMultipleNodes 限制的类型跳过 </summary>
        private void InsertDuplicateNodes(XNode.Node[] nodes, Vector2 topLeft) {
            if (nodes == null || nodes.Length == 0) return;

            // 以左上角节点为基准计算整体偏移
            Vector2 topLeftNode = nodes[0].position;
            for (int i = 1; i < nodes.Length; i++) {
                topLeftNode = Vector2.Min(topLeftNode, nodes[i].position);
            }
            Vector2 offset = topLeft - topLeftNode;

            UnityEngine.Object[] newNodes = new UnityEngine.Object[nodes.Length];
            Dictionary<XNode.Node, XNode.Node> substitutes = new Dictionary<XNode.Node, XNode.Node>();
            for (int i = 0; i < nodes.Length; i++) {
                XNode.Node srcNode = nodes[i];
                if (srcNode == null) continue;

                // 检查该类型节点是否已达数量上限
                XNode.Node.DisallowMultipleNodesAttribute disallowAttrib;
                Type nodeType = srcNode.GetType();
                if (NodeEditorUtilities.GetAttrib(nodeType, out disallowAttrib)) {
                    int typeCount = 0;
                    for (int j = 0; j < graph.nodes.Count; j++) {
                        if (graph.nodes[j] != null && graph.nodes[j].GetType() == nodeType) typeCount++;
                    }
                    if (typeCount >= disallowAttrib.max) continue;
                }

                XNode.Node newNode = graphEditor.CopyNode(srcNode);
                substitutes.Add(srcNode, newNode);
                newNode.position = srcNode.position + offset;
                newNodes[i] = newNode;
            }

            // 再遍历一轮，按新旧节点映射重建连接
            for (int i = 0; i < nodes.Length; i++) {
                XNode.Node srcNode = nodes[i];
                if (srcNode == null) continue;
                foreach (XNode.NodePort port in srcNode.Ports) {
                    for (int c = 0; c < port.ConnectionCount; c++) {
                        XNode.NodePort inputPort = port.direction == XNode.NodePort.IO.Input ? port : port.GetConnection(c);
                        XNode.NodePort outputPort = port.direction == XNode.NodePort.IO.Output ? port : port.GetConnection(c);

                        XNode.Node newNodeIn, newNodeOut;
                        if (substitutes.TryGetValue(inputPort.node, out newNodeIn) && substitutes.TryGetValue(outputPort.node, out newNodeOut)) {
                            newNodeIn.UpdatePorts();
                            newNodeOut.UpdatePorts();
                            inputPort = newNodeIn.GetInputPort(inputPort.fieldName);
                            outputPort = newNodeOut.GetOutputPort(outputPort.fieldName);
                        }
                        if (!inputPort.IsConnectedTo(outputPort)) inputPort.Connect(outputPort);
                    }
                }
            }
            EditorUtility.SetDirty(graph);
            // 选中新节点
            Selection.objects = newNodes;
        }

        /// <summary> 绘制正在拖拽中的连线 </summary>
        public void DrawDraggedConnection() {
            if (IsDraggingPort) {
                Gradient gradient = graphEditor.GetNoodleGradient(draggedOutput, null);
                float thickness = graphEditor.GetNoodleThickness(draggedOutput, null);
                NoodlePath path = graphEditor.GetNoodlePath(draggedOutput, null);
                NoodleStroke stroke = graphEditor.GetNoodleStroke(draggedOutput, null);

                // 拖线的路径点：起点锚点 + 途中重路由点 + 鼠标/目标端口
                Rect fromRect;
                if (!_portConnectionPoints.TryGetValue(draggedOutput, out fromRect)) return;
                draggedConnectionGridPoints.Clear();
                draggedConnectionGridPoints.Add(fromRect.center);
                for (int i = 0; i < draggedOutputReroutes.Count; i++) {
                    draggedConnectionGridPoints.Add(draggedOutputReroutes[i]);
                }
                if (draggedOutputTarget != null) draggedConnectionGridPoints.Add(portConnectionPoints[draggedOutputTarget].center);
                else draggedConnectionGridPoints.Add(WindowToGridPosition(Event.current.mousePosition));

                DrawNoodle(gradient, path, stroke, thickness, draggedConnectionGridPoints);

                GUIStyle portStyle = NodeEditorWindow.current.graphEditor.GetPortStyle(draggedOutput);
                Color bgcol = Color.black;
                Color frcol = graphEditor.GetPortColor(draggedOutput);
                bgcol.a = 0.6f;
                frcol.a = 0.6f;

                // 重绘拖线上的重路由点
                for (int i = 0; i < draggedOutputReroutes.Count; i++) {
                    Rect rect = new Rect(draggedOutputReroutes[i], new Vector2(16, 16));
                    rect.position = new Vector2(rect.position.x - 8, rect.position.y - 8);
                    rect = GridToWindowRect(rect);

                    NodeEditorGUILayout.DrawPortHandle(rect, bgcol, frcol, portStyle.normal.background, portStyle.active.background);
                }
            }
        }

        /// <summary> 鼠标是否悬停在节点标题栏上 </summary>
        bool IsHoveringTitle(XNode.Node node) {
            Vector2 mousePos = Event.current.mousePosition;
            Vector2 nodePos = GridToWindowPosition(node.position);
            float width;
            Vector2 size;
            if (nodeSizes.TryGetValue(node, out size)) width = size.x;
            else width = 200;
            Rect windowRect = new Rect(nodePos, new Vector2(width / zoom, 30 / zoom));
            return windowRect.Contains(mousePos);
        }

        /// <summary> 新建节点落在画布上时，尝试把它自动连到拖线起点对应的输入端口 </summary>
        public void AutoConnect(XNode.Node node) {
            if (autoConnectOutput == null) return;

            // 找到第一个兼容的输入端口并连接
            foreach (XNode.NodePort port in node.Ports) {
                if (port.IsInput && graphEditor.CanConnect(autoConnectOutput, port)) {
                    autoConnectOutput.Connect(port);
                    break;
                }
            }

            EditorUtility.SetDirty(graph);
            if (NodeEditorPreferences.GetSettings().autoSave) AssetDatabase.SaveAssets();
            autoConnectOutput = null;
        }
    }
}
