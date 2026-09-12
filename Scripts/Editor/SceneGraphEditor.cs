using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XNode;

namespace XNodeEditor {
    /// <summary> SceneGraph 组件的检视器：提供新建/打开/移除图按钮 </summary>
    [CustomEditor(typeof(SceneGraph), true)]
    public class SceneGraphEditor : Editor {
        private SceneGraph sceneGraph;
        private bool removeSafely;
        private Type graphType;

        /// <summary>
        /// 无图时显示"New graph"（非泛型 SceneGraph 先弹图类型选择菜单）；
        /// 有图时显示"Open graph"与带二次确认的"Remove graph"，最后补画默认检视器。
        /// </summary>
        public override void OnInspectorGUI() {
            if (sceneGraph.graph == null) {
                if (GUILayout.Button("New graph", GUILayout.Height(40))) {
                    if (graphType == null) {
                        Type[] graphTypes = NodeEditorReflection.GetDerivedTypes(typeof(NodeGraph));
                        GenericMenu menu = new GenericMenu();
                        for (int i = 0; i < graphTypes.Length; i++) {
                            Type graphType = graphTypes[i];
                            menu.AddItem(new GUIContent(graphType.Name), false, () => CreateGraph(graphType));
                        }
                        menu.ShowAsContext();
                    } else {
                        CreateGraph(graphType);
                    }
                }
            } else {
                if (GUILayout.Button("Open graph", GUILayout.Height(40))) {
                    NodeEditorWindow.Open(sceneGraph.graph);
                }
                if (removeSafely) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Really remove graph?");
                    GUI.color = new Color(1, 0.8f, 0.8f);
                    if (GUILayout.Button("Remove")) {
                        removeSafely = false;
                        Undo.RecordObject(sceneGraph, "Removed graph");
                        sceneGraph.graph = null;
                    }
                    GUI.color = Color.white;
                    if (GUILayout.Button("Cancel")) {
                        removeSafely = false;
                    }
                    GUILayout.EndHorizontal();
                } else {
                    GUI.color = new Color(1, 0.8f, 0.8f);
                    if (GUILayout.Button("Remove graph")) {
                        removeSafely = true;
                    }
                    GUI.color = Color.white;
                }
            }
			DrawDefaultInspector();
        }

        /// <summary> 缓存目标组件；SceneGraph&lt;T&gt; 派生类从泛型实参取出图类型，新建图时不再弹类型选择 </summary>
        private void OnEnable() {
            sceneGraph = target as SceneGraph;
            Type sceneGraphType = sceneGraph.GetType();
            if (sceneGraphType == typeof(SceneGraph)) {
                graphType = null;
            } else {
                Type baseType = sceneGraphType.BaseType;
                if (baseType.IsGenericType) {
                    graphType = sceneGraphType = baseType.GetGenericArguments() [0];
                }
            }
        }

        /// <summary> 实例化指定类型的图挂到场景组件上，并按组件名命名图资产（可撤销） </summary>
        public void CreateGraph(Type type) {
            Undo.RecordObject(sceneGraph, "Create graph");
            sceneGraph.graph = ScriptableObject.CreateInstance(type) as NodeGraph;
            sceneGraph.graph.name = sceneGraph.name + "-graph";
        }
    }
}