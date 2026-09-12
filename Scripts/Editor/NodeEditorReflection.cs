using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#if UNITY_2019_1_OR_NEWER && USE_ADVANCED_GENERIC_MENU
using GenericMenu = XNodeEditor.AdvancedGenericMenu;
#endif

namespace XNodeEditor {
    /// <summary> Contains reflection-related extensions built for xNode </summary>
    public static class NodeEditorReflection {
        [NonSerialized] private static Dictionary<Type, Color> nodeTint;
        [NonSerialized] private static Dictionary<Type, int> nodeWidth;
        /// <summary> All available node types </summary>
        public static Type[] nodeTypes { get { return _nodeTypes != null ? _nodeTypes : _nodeTypes = GetNodeTypes(); } }

        [NonSerialized] private static Type[] _nodeTypes = null;

        /// <summary> 返回判断窗口是否停靠的委托；缓存委托比每次反射调用更快 </summary>
        public static Func<bool> GetIsDockedDelegate(this EditorWindow window) {
            BindingFlags fullBinding = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            MethodInfo isDockedMethod = typeof(EditorWindow).GetProperty("docked", fullBinding).GetGetMethod(true);
            return (Func<bool>) Delegate.CreateDelegate(typeof(Func<bool>), window, isDockedMethod);
        }

        /// <summary> 反射取全部 Node 派生类型（进程内缓存，右键创建菜单的数据源） </summary>
        public static Type[] GetNodeTypes() {
            //反射取全部 Node 派生类型
            return GetDerivedTypes(typeof(XNode.Node));
        }

        /// <summary> 取 [NodeTint(r, g, b)] 定义的节点着色 </summary>
        public static bool TryGetAttributeTint(this Type nodeType, out Color tint) {
            if (nodeTint == null) {
                CacheAttributes<Color, XNode.Node.NodeTintAttribute>(ref nodeTint, x => x.color);
            }
            return nodeTint.TryGetValue(nodeType, out tint);
        }

        /// <summary> 取 [NodeWidth(width)] 定义的节点宽度 </summary>
        public static bool TryGetAttributeWidth(this Type nodeType, out int width) {
            if (nodeWidth == null) {
                CacheAttributes<int, XNode.Node.NodeWidthAttribute>(ref nodeWidth, x => x.width);
            }
            return nodeWidth.TryGetValue(nodeType, out width);
        }

        private static void CacheAttributes<V, A>(ref Dictionary<Type, V> dict, Func<A, V> getter) where A : Attribute {
            dict = new Dictionary<Type, V>();
            for (int i = 0; i < nodeTypes.Length; i++) {
                object[] attribs = nodeTypes[i].GetCustomAttributes(typeof(A), true);
                if (attribs == null || attribs.Length == 0) continue;
                A attrib = attribs[0] as A;
                dict.Add(nodeTypes[i], getter(attrib));
            }
        }

        /// <summary> Get FieldInfo of a field, including those that are private and/or inherited </summary>
        public static FieldInfo GetFieldInfo(this Type type, string fieldName) {
            // If we can't find field in the first run, it's probably a private field in a base class.
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            // Search base classes for private fields only. Public fields are found above
            while (field == null && (type = type.BaseType) != typeof(XNode.Node)) field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return field;
        }

        /// <summary> 反射取所有继承自基类的类型 </summary>
        public static Type[] GetDerivedTypes(this Type baseType) {
            List<System.Type> types = new List<System.Type>();
            System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies) {
                try {
                    types.AddRange(assembly.GetTypes().Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t)).ToArray());
                } catch (ReflectionTypeLoadException) { }
            }
            return types.ToArray();
        }

        /// <summary> Find methods marked with the [ContextMenu] attribute and add them to the context menu </summary>
        public static void AddCustomContextMenuItems(this GenericMenu contextMenu, object obj) {
            KeyValuePair<ContextMenu, MethodInfo>[] items = GetContextMenuMethods(obj);
            if (items.Length != 0) {
                contextMenu.AddSeparator("");
                List<string> invalidatedEntries = new List<string>();
                foreach (KeyValuePair<ContextMenu, MethodInfo> checkValidate in items) {
                    if (checkValidate.Key.validate && !(bool) checkValidate.Value.Invoke(obj, null)) {
                        invalidatedEntries.Add(checkValidate.Key.menuItem);
                    }
                }
                for (int i = 0; i < items.Length; i++) {
                    KeyValuePair<ContextMenu, MethodInfo> kvp = items[i];
                    if (invalidatedEntries.Contains(kvp.Key.menuItem)) {
                        contextMenu.AddDisabledItem(new GUIContent(kvp.Key.menuItem));
                    } else {
                        contextMenu.AddItem(new GUIContent(kvp.Key.menuItem), false, () => kvp.Value.Invoke(obj, null));
                    }
                }
            }
        }

        /// <summary> 对目标调用 OnValidate </summary>
        public static void TriggerOnValidate(this UnityEngine.Object target) {
            System.Reflection.MethodInfo onValidate = null;
            if (target != null) {
                onValidate = target.GetType().GetMethod("OnValidate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (onValidate != null) onValidate.Invoke(target, null);
            }
        }

        /// <summary>
        /// 反射取对象上全部标了 [ContextMenu] 的实例方法；带参数或静态方法会告警并跳过，
        /// 结果按 ContextMenu.priority 升序排序。
        /// </summary>
        public static KeyValuePair<ContextMenu, MethodInfo>[] GetContextMenuMethods(object obj) {
            Type type = obj.GetType();
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            List<KeyValuePair<ContextMenu, MethodInfo>> kvp = new List<KeyValuePair<ContextMenu, MethodInfo>>();
            for (int i = 0; i < methods.Length; i++) {
                ContextMenu[] attribs = methods[i].GetCustomAttributes(typeof(ContextMenu), true).Select(x => x as ContextMenu).ToArray();
                if (attribs == null || attribs.Length == 0) continue;
                if (methods[i].GetParameters().Length != 0) {
                    Debug.LogWarning("Method " + methods[i].DeclaringType.Name + "." + methods[i].Name + " has parameters and cannot be used for context menu commands.");
                    continue;
                }
                if (methods[i].IsStatic) {
                    Debug.LogWarning("Method " + methods[i].DeclaringType.Name + "." + methods[i].Name + " is static and cannot be used for context menu commands.");
                    continue;
                }

                for (int k = 0; k < attribs.Length; k++) {
                    kvp.Add(new KeyValuePair<ContextMenu, MethodInfo>(attribs[k], methods[i]));
                }
            }
            // 按优先级排序菜单项
            kvp.Sort((x, y) => x.Key.priority.CompareTo(y.Key.priority));
            return kvp.ToArray();
        }

        /// <summary> 打开 xNode 偏好设置页；内部 API 变动时会失败并提示 </summary>
        public static void OpenPreferences() {
            try {
                SettingsService.OpenUserPreferences("Preferences/Node Editor");
            } catch (Exception e) {
                Debug.LogError(e);
                Debug.LogWarning("Unity 内部结构已变更，无法通过反射打开偏好设置。请向 xNode 开发者反馈并附上 Unity 版本号。");
            }
        }
    }
}
