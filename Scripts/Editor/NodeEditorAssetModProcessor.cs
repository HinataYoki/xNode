using UnityEditor;
using UnityEngine;
using System.IO;

namespace XNodeEditor {
    /// <summary> 处理资产修改事件 </summary>
    class NodeEditorAssetModProcessor : UnityEditor.AssetModificationProcessor {

        /// <summary> 删除节点脚本前自动删除其节点子资产。
        /// 这一步很关键，因为 null 子资产无法手动删除。
        /// <para/> 其它变通方案见: https://gitlab.com/RotaryHeart-UnityShare/subassetmissingscriptdelete </summary>
        private static AssetDeleteResult OnWillDeleteAsset (string path, RemoveAssetOptions options) {
            // 跳过非 .cs 文件
            if (Path.GetExtension(path) != ".cs") return AssetDeleteResult.DidNotDelete;

            // 取要删除的对象
            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object> (path);

            // 不是脚本则跳过
            if (!(obj is UnityEditor.MonoScript)) return AssetDeleteResult.DidNotDelete;

            // 校验脚本类型：非 Node 脚本直接放行
            UnityEditor.MonoScript script = obj as UnityEditor.MonoScript;
            System.Type scriptType = script.GetClass ();
            if (scriptType == null || (scriptType != typeof (XNode.Node) && !scriptType.IsSubclassOf (typeof (XNode.Node)))) return AssetDeleteResult.DidNotDelete;

            // 找到所有使用该脚本的 ScriptableObject
            string[] guids = AssetDatabase.FindAssets ("t:" + scriptType);
            for (int i = 0; i < guids.Length; i++) {
                string assetpath = AssetDatabase.GUIDToAssetPath (guids[i]);
                Object[] objs = AssetDatabase.LoadAllAssetRepresentationsAtPath (assetpath);
                for (int k = 0; k < objs.Length; k++) {
                    XNode.Node node = objs[k] as XNode.Node;
                    if (node.GetType () == scriptType) {
                        if (node != null && node.graph != null) {
                            // Delete the node and notify the user
                            Debug.LogWarning (node.name + " of " + node.graph + " depended on deleted script and has been removed automatically.", node.graph);
                            node.graph.RemoveNode (node);
                        }
                    }
                }
            }
            // 本处理器没有真正删除脚本，交回系统继续正常删除流程
            return AssetDeleteResult.DidNotDelete;
        }

        /// <summary> 编辑器重载后，自动把游离的节点子资产补回图的节点列表 </summary>
        [InitializeOnLoadMethod]
        private static void OnReloadEditor () {
            // 查找全部 NodeGraph 资产
            string[] guids = AssetDatabase.FindAssets ("t:" + typeof (XNode.NodeGraph));
            for (int i = 0; i < guids.Length; i++) {
                string assetpath = AssetDatabase.GUIDToAssetPath (guids[i]);
                XNode.NodeGraph graph = AssetDatabase.LoadAssetAtPath (assetpath, typeof (XNode.NodeGraph)) as XNode.NodeGraph;
                if (graph == null) continue;

                bool changed = graph.nodes.RemoveAll(x => x == null) > 0; // 移除空项
                Object[] objs = AssetDatabase.LoadAllAssetRepresentationsAtPath (assetpath);
                // 确保全部节点子资产都在图的节点列表里
                for (int u = 0; u < objs.Length; u++) {
                    // 忽略 null 子资产
                    XNode.Node node = objs[u] as XNode.Node;
                    if (node == null) continue;
                    if (!graph.nodes.Contains (node)) {
                        graph.nodes.Add(node);
                        changed = true;
                    }
                }
                if (changed) EditorUtility.SetDirty(graph);
            }
        }
    }
}
