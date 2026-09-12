using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
#endif

namespace XNodeEditor.Internal {
	/// <summary> 负责缓存自定义编辑器类及其目标类型。可通过 GetEditor(Type type) 访问 </summary>
	/// <typeparam name="T">编辑器类型。应为派生脚本自身的类型（如 NodeEditor） </typeparam>
	/// <typeparam name="A">特性类型。用于关联运行时类型的特性（如 CustomNodeEditorAttribute） </typeparam>
	/// <typeparam name="K">运行时类型。此编辑器可编辑的 ScriptableObject（如 Node） </typeparam>
	public abstract class NodeEditorBase<T, A, K> where A : Attribute, NodeEditorBase<T, A, K>.INodeEditorAttrib where T : NodeEditorBase<T, A, K> where K : ScriptableObject {
		/// <summary> 用 [CustomNodeEditor] 定义的自定义编辑器 </summary>
		private static Dictionary<Type, Type> editorTypes;
		private static Dictionary<K, T> editors = new Dictionary<K, T>();
		public NodeEditorWindow window;
		public K target;
		public SerializedObject serializedObject;
#if ODIN_INSPECTOR
		private PropertyTree _objectTree;
		public PropertyTree objectTree {
			get {
                if (this._objectTree == null){
					try {
						bool wasInEditor = NodeEditor.inNodeEditor;
						NodeEditor.inNodeEditor = true;
						this._objectTree = PropertyTree.Create(this.serializedObject);
						NodeEditor.inNodeEditor = wasInEditor;
					} catch (ArgumentException ex) {
						Debug.Log(ex);
					}
				}
				return this._objectTree;
			}
		}
#endif

		/// <summary> 清理 target 已销毁的编辑器缓存条目；Unity 对象销毁后仍留托管引用，需按假 null 移除 </summary>
		public static void CleanupDestroyedEditors() {
			List<K> destroyed = null;
			foreach (KeyValuePair<K, T> pair in editors) {
				if (pair.Key == null) {
					if (destroyed == null) destroyed = new List<K>();
					destroyed.Add(pair.Key);
				}
			}
			if (destroyed != null) {
				for (int i = 0; i < destroyed.Count; i++) editors.Remove(destroyed[i]);
			}
		}

		/// <summary>
		/// 取目标的编辑器实例：缓存命中直接返回；未命中则按目标类型解析编辑器类型、
		/// 反射创建并缓存。返回前顺带修复 target/window/serializedObject 与当前状态的漂移。
		/// </summary>
		public static T GetEditor(K target, NodeEditorWindow window) {
			if (target == null) return null;
			T editor;
			if (!editors.TryGetValue(target, out editor)) {
				Type type = target.GetType();
				Type editorType = GetEditorType(type);
				editor = Activator.CreateInstance(editorType) as T;
				editor.target = target;
				editor.serializedObject = new SerializedObject(target);
				editor.window = window;
				editor.OnCreate();
				editors.Add(target, editor);
			}
			if (editor.target == null) editor.target = target;
			if (editor.window != window) editor.window = window;
			if (editor.serializedObject == null) editor.serializedObject = new SerializedObject(target);
			return editor;
		}

	    /// <summary> 从缓存移除目标的编辑器实例；Odin 属性树失效等场景下调用以强制重建 </summary>
        public static void DestroyEditor( K target )
        {
            if ( target == null ) return;
            T editor;
            if ( editors.TryGetValue( target, out editor ) )
            {
                editors.Remove( target );
            }
        }

		/// <summary> 沿目标继承链向上查找已注册的自定义编辑器类型；走到 null 返回 null </summary>
		private static Type GetEditorType(Type type) {
			if (type == null) return null;
			if (editorTypes == null) CacheCustomEditors();
			Type result;
			if (editorTypes.TryGetValue(type, out result)) return result;
			//找不到类型时，尝试其基类型
			return GetEditorType(type.BaseType);
		}

		/// <summary> 反射扫描全部非抽象编辑器派生类，按其特性建立 目标类型 -> 编辑器类型 映射缓存 </summary>
		private static void CacheCustomEditors() {
			editorTypes = new Dictionary<Type, Type>();

			//通过反射获取所有从 NodeEditor 派生的类
			Type[] nodeEditors = typeof(T).GetDerivedTypes();
			for (int i = 0; i < nodeEditors.Length; i++) {
				if (nodeEditors[i].IsAbstract) continue;
				var attribs = nodeEditors[i].GetCustomAttributes(typeof(A), false);
				if (attribs == null || attribs.Length == 0) continue;
				A attrib = attribs[0] as A;
				editorTypes.Add(attrib.GetInspectedType(), nodeEditors[i]);
			}
		}

		/// <summary> 创建时调用，此时各引用已设置完毕 </summary>
		public virtual void OnCreate() { }

		public interface INodeEditorAttrib {
			Type GetInspectedType();
		}
	}
}