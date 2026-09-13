#if UNITY_EDITOR && ODIN_INSPECTOR
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEngine;
using XNode;

namespace XNodeEditor {
	/// <summary> Odin 对 [Output] 字段的绘制器：在节点编辑器内画出带输出端口的属性字段 </summary>
	public class OutputAttributeDrawer : OdinAttributeDrawer<XNode.Node.OutputAttribute> {
		/// <summary> 目标树的弱引用能解析为 Node 时才启用本绘制器 </summary>
		protected override bool CanDrawAttributeProperty(InspectorProperty property) {
			Node node = property.Tree.WeakTargets[0] as Node;
			return node != null;
		}

		/// <summary>
		/// 非 Odin 节点编辑器环境时按 backingValue 决定是否放行到下一绘制器；
		/// 节点编辑器内：多选告警、取不到 Unity 序列化属性时报错，
		/// 否则按 LabelWidth 特性推送标签宽度并调用 NodeEditorGUILayout.PropertyField 画端口字段。
		/// </summary>
		protected override void DrawPropertyLayout(GUIContent label) {
			Node node = Property.Tree.WeakTargets[0] as Node;
			NodePort port = node.GetOutputPort(Property.Name);

			if (!NodeEditor.inNodeEditor) {
				if (Attribute.backingValue == XNode.Node.ShowBackingValue.Always
					|| Attribute.backingValue == XNode.Node.ShowBackingValue.Unconnected && (port == null || !port.IsConnected))
					CallNextDrawer(label);
				return;
			}

			if (Property.Tree.WeakTargets.Count > 1) {
				SirenixEditorGUI.WarningMessageBox("Cannot draw ports with multiple nodes selected");
				return;
			}

			if (port != null) {
				var portPropoerty = Property.Tree.GetUnityPropertyForPath(Property.UnityPropertyPath);
				if (portPropoerty == null) {
					SirenixEditorGUI.ErrorMessageBox("Port property missing at: " + Property.UnityPropertyPath);
					return;
				} else {
					var labelWidth = Property.GetAttribute<LabelWidthAttribute>();
					if (labelWidth != null)
						GUIHelper.PushLabelWidth(labelWidth.Width);

					NodeEditorGUILayout.PropertyField(portPropoerty, label == null ? GUIContent.none : label, true, GUILayout.MinWidth(30));

					if (labelWidth != null)
						GUIHelper.PopLabelWidth();
				}
			}
		}
	}
}
#endif