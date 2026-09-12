#if UNITY_EDITOR && ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector.Editor;
using UnityEngine;
using XNode;

namespace XNodeEditor {
	/// <summary>
	/// Odin 特性处理器：仅当 Odin 在节点编辑器内绘制 Node 成员时生效，
	/// 把 graph/position/ports 三个内部字段从 Inspector 里隐藏。
	/// </summary>
	internal class OdinNodeInGraphAttributeProcessor<T> : OdinAttributeProcessor<T> where T : Node {
		/// <summary> 不处理节点自身的特性，只处理成员 </summary>
		public override bool CanProcessSelfAttributes(InspectorProperty property) {
			return false;
		}

		/// <summary> 仅在节点编辑器内处理字段成员，且只针对 graph/position/ports 三个内部字段 </summary>
		public override bool CanProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member) {
			if (!NodeEditor.inNodeEditor)
				return false;

			if (member.MemberType == MemberTypes.Field) {
				switch (member.Name) {
					case "graph":
					case "position":
					case "ports":
						return true;

					default:
						break;
				}
			}

			return false;
		}

		/// <summary> 对 graph/position/ports 注入 HideInInspector，在 Odin 绘制的节点面板中隐藏 </summary>
		public override void ProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member, List<Attribute> attributes) {
			switch (member.Name) {
				case "graph":
				case "position":
				case "ports":
					attributes.Add(new HideInInspector());
					break;

				default:
					break;
			}
		}
	}
}
#endif