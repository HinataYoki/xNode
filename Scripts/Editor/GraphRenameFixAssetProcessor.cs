using UnityEditor;
using XNode;

namespace XNodeEditor {
    /// <summary>
    /// 修复 2019.3+ 的 v2 AssetDatabase 问题：重命名 <see cref="XNode.NodeGraph"/> 资产时，
    /// v2 AssetDatabase 偶尔会把 <see cref="XNode.NodeGraph"/> 与其某个 <see cref="XNode.Node"/>
    /// 子资产的主资产身份互换。在 Unity 修复前，本处理器检查所有被重命名的资产，
    /// 发现节点被设为主资产时把图换回主资产，并把节点名重置为该类型的默认名。
    /// </summary>
    internal sealed class GraphRenameFixAssetProcessor : AssetPostprocessor {
        /// <summary> 资产移动/重命名后处理：发现主资产身份被子资产节点抢占时，把图换回主资产并重置节点名 </summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths) {
            for (int i = 0; i < movedAssets.Length; i++) {
                Node nodeAsset = AssetDatabase.LoadMainAssetAtPath(movedAssets[i]) as Node;

                // 图资产的主资产身份被子资产节点抢占时，把图换回主资产并重置节点名
                if (nodeAsset != null && nodeAsset.graph != null && AssetDatabase.IsMainAsset(nodeAsset)) {
                    AssetDatabase.SetMainObject(nodeAsset.graph, movedAssets[i]);
                    AssetDatabase.ImportAsset(movedAssets[i]);

                    nodeAsset.name = NodeEditorUtilities.NodeDefaultName(nodeAsset.GetType());
                    EditorUtility.SetDirty(nodeAsset);
                }
            }
        }
    }
}