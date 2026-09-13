using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using UnityEngine;
using XNode;

namespace XNodeEditor {
    /// <summary> 处理被修改的资产 </summary>
    class NodeGraphImporter : AssetPostprocessor {
        /// <summary> 资产导入后处理：检查图类型上的 [RequireNode] 特性，补建缺失的必需节点 </summary>
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
            foreach (string path in importedAssets) {
                // 跳过非 .asset 文件
                if (Path.GetExtension(path) != ".asset") continue;

                // 加载本次导入的资产
                NodeGraph graph = AssetDatabase.LoadAssetAtPath<NodeGraph>(path);
                if (graph == null) continue;

                // 取 RequireNode 特性
                Type graphType = graph.GetType();
                NodeGraph.RequireNodeAttribute[] attribs = Array.ConvertAll(
                    graphType.GetCustomAttributes(typeof(NodeGraph.RequireNodeAttribute), true), x => x as NodeGraph.RequireNodeAttribute);

                Vector2 position = Vector2.zero;
                foreach (NodeGraph.RequireNodeAttribute attrib in attribs) {
                    if (attrib.type0 != null) AddRequired(graph, attrib.type0, ref position);
                    if (attrib.type1 != null) AddRequired(graph, attrib.type1, ref position);
                    if (attrib.type2 != null) AddRequired(graph, attrib.type2, ref position);
                }
            }
        }

        /// <summary> 图中不存在该类型的节点时在 position 处补建一个，命名并挂为图资产的子资产 </summary>
        private static void AddRequired(NodeGraph graph, Type type, ref Vector2 position) {
            if (!graph.nodes.Any(x => x != null && x.GetType() == type)) {
                XNode.Node node = graph.AddNode(type);
                node.position = position;
                position.x += 200;
                if (node.name == null || node.name.Trim() == "") node.name = NodeEditorUtilities.NodeDefaultName(type);
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(graph))) AssetDatabase.AddObjectToAsset(node, graph);
                EditorUtility.SetDirty(graph);
            }
        }
    }
}