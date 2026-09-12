using UnityEditor;
using UnityEngine;

namespace XNodeEditor {
    /// <summary> 资产重命名弹窗工具 </summary>
    public class RenamePopup : EditorWindow {
        private const string inputControlName = "nameInput";

        public static RenamePopup current { get; private set; }
        public Object target;
        public string input;

        private bool firstFrame = true;

        /// <summary> 在鼠标位置弹出资产重命名窗口；确认时触发资产重导入。 </summary>
        public static RenamePopup Show(Object target, float width = 200) {
            RenamePopup window = EditorWindow.GetWindow<RenamePopup>(true, "Rename " + target.name, true);
            if (current != null) current.Close();
            current = window;
            window.target = target;
            window.input = target.name;
            window.minSize = new Vector2(100, 44);
            window.position = new Rect(0, 0, width, 44);
            window.UpdatePositionToMouse();
            return window;
        }

        /// <summary> 把窗口定位到鼠标下方水平居中；Event.current 为空时跳过 </summary>
        private void UpdatePositionToMouse() {
            if (Event.current == null) return;
            Vector3 mousePoint = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            Rect pos = position;
            pos.x = mousePoint.x - position.width * 0.5f;
            pos.y = mousePoint.y - 10;
            position = pos;
        }

        /// <summary> 失焦时自动关闭弹窗 </summary>
        private void OnLostFocus() {
            // 失焦时自动关闭弹窗
            Close();
        }

        /// <summary>
        /// 绘制重命名输入框：空输入时按钮变为"还原默认名"，回车/Apply 提交；
        /// 提交后触发 OnRename、重设图为主资产并重导入；Esc 直接关闭。
        /// </summary>
        private void OnGUI() {
            if (firstFrame) {
                UpdatePositionToMouse();
                firstFrame = false;
            }
            GUI.SetNextControlName(inputControlName);
            input = EditorGUILayout.TextField(input);
            EditorGUI.FocusTextInControl(inputControlName);
            Event e = Event.current;
            // 输入为空时改为还原默认名
            if (input == null || input.Trim() == "") {
                if (GUILayout.Button("Revert to default") || (e.isKey && e.keyCode == KeyCode.Return)) {
                    target.name = NodeEditorUtilities.NodeDefaultName(target.GetType());
                    NodeEditor.GetEditor((XNode.Node)target, NodeEditorWindow.current).OnRename();
                    if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target))) {
                        AssetDatabase.SetMainObject((target as XNode.Node).graph, AssetDatabase.GetAssetPath(target));
                        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(target));
                    }
                    Close();
                    target.TriggerOnValidate();
                }
            }
            // 按输入文本重命名资产
            else {
                if (GUILayout.Button("Apply") || (e.isKey && e.keyCode == KeyCode.Return)) {
                    target.name = input;
                    NodeEditor.GetEditor((XNode.Node)target, NodeEditorWindow.current).OnRename();
                    if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target))) {
                        AssetDatabase.SetMainObject((target as XNode.Node).graph, AssetDatabase.GetAssetPath(target));
                        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(target));
                    }
                    Close();
                    target.TriggerOnValidate();
                }
            }

            if (e.isKey && e.keyCode == KeyCode.Escape) {
                Close();
            }
        }

        /// <summary> 关闭弹窗时退出 IMGUI 文本编辑态，避免编辑框焦点残留 </summary>
        private void OnDestroy() {
            EditorGUIUtility.editingTextField = false;
        }
    }
}