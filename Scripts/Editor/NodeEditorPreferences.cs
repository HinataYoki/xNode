using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace XNodeEditor {
    public enum NoodlePath { Curvy, Straight, Angled, ShaderLab }
    public enum NoodleStroke { Full, Dashed }

    /// <summary> 节点编辑器偏好设置，按图编辑器类型存取 EditorPrefs </summary>
    public static class NodeEditorPreferences {

        /// <summary> 上次校验的编辑器，后续设置修改作用于它 </summary>
        private static XNodeEditor.NodeGraphEditor lastEditor;
        /// <summary> 上次使用的 EditorPrefs 键，后续设置修改作用于它 </summary>
        private static string lastKey = "xNode.Settings";

        private static Dictionary<Type, Color> typeColors = new Dictionary<Type, Color>();
        private static Dictionary<string, Settings> settings = new Dictionary<string, Settings>();

        [System.Serializable]
        public class Settings : ISerializationCallbackReceiver {
            [SerializeField] private Color32 _gridLineColor = new Color(.23f, .23f, .23f);
            public Color32 gridLineColor { get { return _gridLineColor; } set { _gridLineColor = value; _gridTexture = null; _crossTexture = null; } }

            [SerializeField] private Color32 _gridBgColor = new Color(.19f, .19f, .19f);
            public Color32 gridBgColor { get { return _gridBgColor; } set { _gridBgColor = value; _gridTexture = null; } }

            [Obsolete("Use maxZoom instead")]
            public float zoomOutLimit { get { return maxZoom; } set { maxZoom = value; } }

            [UnityEngine.Serialization.FormerlySerializedAs("zoomOutLimit")]
            public float maxZoom = 5f;
            public float minZoom = 1f;
            /// <summary> 节点默认着色；节点类型未标 [NodeTint] 时使用 </summary>
            public Color32 tintColor = new Color32(90, 97, 105, 255);
            /// <summary> 选中节点高亮描边与选中重路由点的高亮色 </summary>
            public Color32 highlightColor = new Color32(255, 255, 255, 255);
            public bool gridSnap = true;
            public bool autoSave = true;
            public bool openOnCreate = true;
            public bool dragToCreate = true;
            public bool createFilter = true;
            public bool zoomToMouse = true;
            public bool portTooltips = true;
            [SerializeField] private string typeColorsData = "";
            [NonSerialized] public Dictionary<string, Color> typeColors = new Dictionary<string, Color>();
            [FormerlySerializedAs("noodleType")] public NoodlePath noodlePath = NoodlePath.Curvy;
            public float noodleThickness = 2f;

            public NoodleStroke noodleStroke = NoodleStroke.Full;

            private Texture2D _gridTexture;
            public Texture2D gridTexture {
                get {
                    if (_gridTexture == null) _gridTexture = NodeEditorResources.GenerateGridTexture(gridLineColor, gridBgColor);
                    return _gridTexture;
                }
            }
            private Texture2D _crossTexture;
            public Texture2D crossTexture {
                get {
                    if (_crossTexture == null) _crossTexture = NodeEditorResources.GenerateCrossTexture(gridLineColor);
                    return _crossTexture;
                }
            }

            /// <summary> 反序列化回调：把 "名称,HEX颜色,..." 形式的字符串还原为类型颜色字典 </summary>
            public void OnAfterDeserialize() {
                // 反序列化类型颜色表
                typeColors = new Dictionary<string, Color>();
                string[] data = typeColorsData.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < data.Length; i += 2) {
                    Color col;
                    if (ColorUtility.TryParseHtmlString("#" + data[i + 1], out col)) {
                        typeColors.Add(data[i], col);
                    }
                }
            }

            /// <summary> 序列化回调：把类型颜色字典拍平成 "名称,HEX颜色,..." 字符串供 JsonUtility 持久化 </summary>
            public void OnBeforeSerialize() {
                // 序列化类型颜色表
                typeColorsData = "";
                foreach (var item in typeColors) {
                    typeColorsData += item.Key + "," + ColorUtility.ToHtmlStringRGB(item.Value) + ",";
                }
            }
        }

        /// <summary> 取当前活动图编辑器的设置；无活动窗口时返回默认值 </summary>
        public static Settings GetSettings() {
            if (XNodeEditor.NodeEditorWindow.current == null) return new Settings();

            if (lastEditor != XNodeEditor.NodeEditorWindow.current.graphEditor) {
                object[] attribs = XNodeEditor.NodeEditorWindow.current.graphEditor.GetType().GetCustomAttributes(typeof(XNodeEditor.NodeGraphEditor.CustomNodeGraphEditorAttribute), true);
                if (attribs.Length == 1) {
                    XNodeEditor.NodeGraphEditor.CustomNodeGraphEditorAttribute attrib = attribs[0] as XNodeEditor.NodeGraphEditor.CustomNodeGraphEditorAttribute;
                    lastEditor = XNodeEditor.NodeEditorWindow.current.graphEditor;
                    lastKey = attrib.editorPrefsKey;
                } else return null;
            }
            if (!settings.ContainsKey(lastKey)) VerifyLoaded();
            return settings[lastKey];
        }

#if UNITY_2019_1_OR_NEWER
        /// <summary> 向 Unity Preferences 注册 "Node Editor" 设置页，绘制回调指向 PreferencesGUI </summary>
        [SettingsProvider]
        public static SettingsProvider CreateXNodeSettingsProvider() {
            SettingsProvider provider = new SettingsProvider("Preferences/Node Editor", SettingsScope.User) {
                guiHandler = (searchContext) => { XNodeEditor.NodeEditorPreferences.PreferencesGUI(); },
                keywords = new HashSet<string>(new [] { "xNode", "node", "editor", "graph", "connections", "noodles", "ports" })
            };
            return provider;
        }
#endif

        /// <summary> 偏好设置页整体绘制：文档链接 + 各分节 + 还原默认按钮 </summary>
        private static void PreferencesGUI() {
            VerifyLoaded();
            Settings settings = NodeEditorPreferences.settings[lastKey];

            if (GUILayout.Button(new GUIContent("Documentation", "https://github.com/Siccity/xNode/wiki"), GUILayout.Width(100))) Application.OpenURL("https://github.com/Siccity/xNode/wiki");
            EditorGUILayout.Space();

            NodeSettingsGUI(lastKey, settings);
            GridSettingsGUI(lastKey, settings);
            SystemSettingsGUI(lastKey, settings);
            TypeColorsGUI(lastKey, settings);
            if (GUILayout.Button(new GUIContent("Set Default", "Reset all values to default"), GUILayout.Width(120))) {
                ResetPrefs();
            }
        }

        /// <summary> 绘制 Grid 分节（吸附/缩放到鼠标/缩放上下限/网格配色），有改动即保存并重绘全部窗口 </summary>
        private static void GridSettingsGUI(string key, Settings settings) {
            EditorGUILayout.LabelField("Grid", EditorStyles.boldLabel);
            settings.gridSnap = EditorGUILayout.Toggle(new GUIContent("Snap", "Hold CTRL in editor to invert"), settings.gridSnap);
            settings.zoomToMouse = EditorGUILayout.Toggle(new GUIContent("Zoom to Mouse", "Zooms towards mouse position"), settings.zoomToMouse);
            EditorGUILayout.LabelField("Zoom");
            EditorGUI.indentLevel++;
            settings.maxZoom = EditorGUILayout.FloatField(new GUIContent("Max", "Upper limit to zoom"), settings.maxZoom);
            settings.minZoom = EditorGUILayout.FloatField(new GUIContent("Min", "Lower limit to zoom"), settings.minZoom);
            EditorGUI.indentLevel--;
            settings.gridLineColor = EditorGUILayout.ColorField("Color", settings.gridLineColor);
            settings.gridBgColor = EditorGUILayout.ColorField(" ", settings.gridBgColor);
            if (GUI.changed) {
                SavePrefs(key, settings);

                NodeEditorWindow.RepaintAll();
            }
            EditorGUILayout.Space();
        }

        /// <summary> 绘制 System 分节（自动保存/创建时自动打开编辑器），有改动即保存 </summary>
        private static void SystemSettingsGUI(string key, Settings settings) {
            EditorGUILayout.LabelField("System", EditorStyles.boldLabel);
            settings.autoSave = EditorGUILayout.Toggle(new GUIContent("Autosave", "Disable for better editor performance"), settings.autoSave);
            settings.openOnCreate = EditorGUILayout.Toggle(new GUIContent("Open Editor on Create", "Disable to prevent openening the editor when creating a new graph"), settings.openOnCreate);
            if (GUI.changed) SavePrefs(key, settings);
            EditorGUILayout.Space();
        }

        /// <summary> 绘制 Node 分节（着色/选中色/连线样式与粗细/端口提示/拖线建节点/创建过滤），有改动即保存并重绘 </summary>
        private static void NodeSettingsGUI(string key, Settings settings) {
            EditorGUILayout.LabelField("Node", EditorStyles.boldLabel);
            settings.tintColor = EditorGUILayout.ColorField("Tint", settings.tintColor);
            settings.highlightColor = EditorGUILayout.ColorField("Selection", settings.highlightColor);
            settings.noodlePath = (NoodlePath) EditorGUILayout.EnumPopup("Noodle path", (Enum) settings.noodlePath);
            settings.noodleThickness = EditorGUILayout.FloatField(new GUIContent("Noodle thickness", "Noodle Thickness of the node connections"), settings.noodleThickness);
            settings.noodleStroke = (NoodleStroke) EditorGUILayout.EnumPopup("Noodle stroke", (Enum) settings.noodleStroke);
            settings.portTooltips = EditorGUILayout.Toggle("Port Tooltips", settings.portTooltips);
            settings.dragToCreate = EditorGUILayout.Toggle(new GUIContent("Drag to Create", "Drag a port connection anywhere on the grid to create and connect a node"), settings.dragToCreate);
            settings.createFilter = EditorGUILayout.Toggle(new GUIContent("Create Filter", "Only show nodes that are compatible with the selected port"), settings.createFilter);

            if (GUI.changed) {
                SavePrefs(key, settings);
                NodeEditorWindow.RepaintAll();
            }
            EditorGUILayout.Space();
        }

        /// <summary> 绘制 Types 分节：逐类型显示颜色编辑框，改动写回设置字典并保存 </summary>
        private static void TypeColorsGUI(string key, Settings settings) {
            EditorGUILayout.LabelField("Types", EditorStyles.boldLabel);

            // 复制键列表，边遍历边修改字典
            var typeColorKeys = new List<Type>(typeColors.Keys);

            // 显示各类型颜色，用户改动时保存
            foreach (var type in typeColorKeys) {
                string typeColorKey = NodeEditorUtilities.PrettyName(type);
                Color col = typeColors[type];
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                col = EditorGUILayout.ColorField(typeColorKey, col);
                EditorGUILayout.EndHorizontal();
                if (EditorGUI.EndChangeCheck()) {
                    typeColors[type] = col;
                    if (settings.typeColors.ContainsKey(typeColorKey)) settings.typeColors[typeColorKey] = col;
                    else settings.typeColors.Add(typeColorKey, col);
                    SavePrefs(key, settings);
                    NodeEditorWindow.RepaintAll();
                }
            }
        }

        /// <summary> 加载偏好设置；不存在时按默认值创建 </summary>
        private static Settings LoadPrefs() {
            if (!EditorPrefs.HasKey(lastKey)) {
                if (lastEditor != null) EditorPrefs.SetString(lastKey, JsonUtility.ToJson(lastEditor.GetDefaultPreferences()));
                else EditorPrefs.SetString(lastKey, JsonUtility.ToJson(new Settings()));
            }
            return JsonUtility.FromJson<Settings>(EditorPrefs.GetString(lastKey));
        }

        /// <summary> 删除全部偏好设置 </summary>
        public static void ResetPrefs() {
            if (EditorPrefs.HasKey(lastKey)) EditorPrefs.DeleteKey(lastKey);
            if (settings.ContainsKey(lastKey)) settings.Remove(lastKey);
            typeColors = new Dictionary<Type, Color>();
            VerifyLoaded();
            NodeEditorWindow.RepaintAll();
        }

        /// <summary> 把偏好设置保存到 EditorPrefs </summary>
        private static void SavePrefs(string key, Settings settings) {
            EditorPrefs.SetString(key, JsonUtility.ToJson(settings));
        }

        /// <summary> 确认指定键的设置已加载，未加载则加载 </summary>
        private static void VerifyLoaded() {
            if (!settings.ContainsKey(lastKey)) settings.Add(lastKey, LoadPrefs());
        }

        /// <summary> 按类型返回颜色；未配置时按类型名哈希生成稳定的随机色 </summary>
        public static Color GetTypeColor(System.Type type) {
            VerifyLoaded();
            if (type == null) return Color.gray;
            Color col;
            if (!typeColors.TryGetValue(type, out col)) {
                string typeName = type.PrettyName();
                if (settings[lastKey].typeColors.ContainsKey(typeName)) typeColors.Add(type, settings[lastKey].typeColors[typeName]);
                else {
                    // 保存/恢复随机状态，避免干扰其它随机逻辑
                    UnityEngine.Random.State oldState = UnityEngine.Random.state;
                    UnityEngine.Random.InitState(typeName.GetHashCode());
                    col = new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value);
                    typeColors.Add(type, col);
                    UnityEngine.Random.state = oldState;
                }
            }
            return col;
        }
    }
}
