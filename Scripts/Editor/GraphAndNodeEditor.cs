using UnityEditor;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
#endif

namespace XNodeEditor {
    /// <summary> 覆写图 Inspector，在顶部显示 "Edit graph" 按钮 </summary>
    [CustomEditor(typeof(XNode.NodeGraph), true)]
#if ODIN_INSPECTOR
    /// <summary> Odin 环境下的图 Inspector：Edit graph 按钮 + Odin 默认绘制 </summary>
    public class GlobalGraphEditor : OdinEditor {
        /// <summary> 顶部画 "Edit graph" 按钮，点击在节点编辑器中打开本图，其余交由 Odin 绘制 </summary>
        public override void OnInspectorGUI() {
            if (GUILayout.Button("Edit graph", GUILayout.Height(40))) {
                NodeEditorWindow.Open(serializedObject.targetObject as XNode.NodeGraph);
            }
            base.OnInspectorGUI();
        }
    }
#else
    [CanEditMultipleObjects]
    /// <summary> 非 Odin 环境下的图 Inspector：Edit graph 按钮 + 原始数据默认绘制 </summary>
    public class GlobalGraphEditor : Editor {
        /// <summary> 顶部画 "Edit graph" 按钮，下方以 "Raw data" 标题画默认检视器 </summary>
        public override void OnInspectorGUI() {
            serializedObject.Update();

            if (GUILayout.Button("Edit graph", GUILayout.Height(40))) {
                NodeEditorWindow.Open(serializedObject.targetObject as XNode.NodeGraph);
            }

            GUILayout.Space(EditorGUIUtility.singleLineHeight);
            GUILayout.Label("Raw data", "BoldLabel");

            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif

    [CustomEditor(typeof(XNode.Node), true)]
#if ODIN_INSPECTOR
    /// <summary> Odin 环境下的节点 Inspector：Edit graph 按钮打开所属图并聚焦本节点 </summary>
    public class GlobalNodeEditor : OdinEditor {
        /// <summary> 顶部画 "Edit graph" 按钮，点击打开所属图窗口并把视角聚焦到本节点 </summary>
        public override void OnInspectorGUI() {
            if (GUILayout.Button("Edit graph", GUILayout.Height(40))) {
                SerializedProperty graphProp = serializedObject.FindProperty("graph");
                NodeEditorWindow w = NodeEditorWindow.Open(graphProp.objectReferenceValue as XNode.NodeGraph);
                if (w != null) w.Home(); // 聚焦选中的节点
            }
            base.OnInspectorGUI();
        }
    }
#else
    [CanEditMultipleObjects]
    /// <summary> 非 Odin 环境下的节点 Inspector：Edit graph 按钮 + 原始数据默认绘制 </summary>
    public class GlobalNodeEditor : Editor {
        /// <summary> 顶部画 "Edit graph" 按钮（打开所属图并聚焦本节点），下方画默认检视器 </summary>
        public override void OnInspectorGUI() {
            serializedObject.Update();

            if (GUILayout.Button("Edit graph", GUILayout.Height(40))) {
                SerializedProperty graphProp = serializedObject.FindProperty("graph");
                NodeEditorWindow w = NodeEditorWindow.Open(graphProp.objectReferenceValue as XNode.NodeGraph);
                if (w != null) w.Home(); // 聚焦选中的节点
            }

            GUILayout.Space(EditorGUIUtility.singleLineHeight);
            GUILayout.Label("Raw data", "BoldLabel");

            // 绘制节点本身的原始数据
            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}