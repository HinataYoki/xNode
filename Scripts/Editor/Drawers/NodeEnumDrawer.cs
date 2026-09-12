using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XNode;
using XNodeEditor;

namespace XNodeEditor {
	/// <summary> [NodeEnum] 特性的绘制器：修正枚举下拉在节点面板里的绘制位置 </summary>
	[CustomPropertyDrawer(typeof(NodeEnumAttribute))]
	public class NodeEnumDrawer : PropertyDrawer {
		/// <summary> PropertyDrawer 入口：绘制属性并附加枚举下拉行为 </summary>
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
			EditorGUI.BeginProperty(position, label, property);

			EnumPopup(position, property, label);

			EditorGUI.EndProperty();
		}

		/// <summary>
		/// 在指定 Rect 绘制枚举下拉按钮；点击不立即弹菜单，而是挂到 onLateGUI 延迟弹出
		/// （节点绘制阶段弹菜单会导致位置错乱）。
		/// </summary>
		public static void EnumPopup(Rect position, SerializedProperty property, GUIContent label) {
			// 类型不对直接报错
			if (property.propertyType != SerializedPropertyType.Enum) {
				throw new ArgumentException("Parameter selected must be of type System.Enum");
			}

			// 绘制前缀标签
			position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

			// 取当前枚举名
			string enumName = "";
			if (property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length) enumName = property.enumDisplayNames[property.enumValueIndex];

#if UNITY_2017_1_OR_NEWER
			// Display dropdown
			if (EditorGUI.DropdownButton(position, new GUIContent(enumName), FocusType.Passive)) {
				// 在节点绘制阶段直接弹菜单会导致位置错乱，挂到 onLateGUI 延迟到本帧末尾显示
				NodeEditorWindow.current.onLateGUI += () => ShowContextMenuAtMouse(property);
			}
#else
			// Display dropdown
			if (GUI.Button(position, new GUIContent(enumName), "MiniPopup")) {
				// Position is all wrong if we show the dropdown during the node draw phase.
				// Instead, add it to onLateGUI to display it later.
				NodeEditorWindow.current.onLateGUI += () => ShowContextMenuAtMouse(property);
			}
#endif
		}

		/// <summary> 在当前鼠标位置弹出枚举选择菜单，选中项写回属性并应用 </summary>
		public static void ShowContextMenuAtMouse(SerializedProperty property) {
			// 初始化菜单
			GenericMenu menu = new GenericMenu();

			// 把所有枚举显示名加入菜单
			for (int i = 0; i < property.enumDisplayNames.Length; i++) {
				int index = i;
				menu.AddItem(new GUIContent(property.enumDisplayNames[i]), false, () => SetEnum(property, index));
			}

			// 在鼠标位置显示
			Rect r = new Rect(Event.current.mousePosition, new Vector2(0, 0));
			menu.DropDown(r);
		}

		/// <summary> 按索引写入枚举值并立即应用/刷新序列化对象 </summary>
		private static void SetEnum(SerializedProperty property, int index) {
			property.enumValueIndex = index;
			property.serializedObject.ApplyModifiedProperties();
			property.serializedObject.Update();
		}
	}
}