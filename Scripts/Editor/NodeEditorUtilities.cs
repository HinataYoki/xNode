using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace XNodeEditor {
    /// <summary> xNode 专用的编辑器工具集与扩展方法 </summary>
    public static class NodeEditorUtilities {

        /// <summary>C# 脚本图标[即 MonoBehaviour 脚本所用的那个图标]。</summary>
        private static Texture2D scriptIcon = (EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D);

        /// 缓存 类型+字段 对应的特性以便快速查找。重新编译后重置。
        private static Dictionary<Type, Dictionary<string, Dictionary<Type, Attribute>>> typeAttributes = new Dictionary<Type, Dictionary<string, Dictionary<Type, Attribute>>>();

        /// 缓存 类型+字段 对应的有序 PropertyAttribute 以便快速查找。重新编译后重置。
        private static Dictionary<Type, Dictionary<string, List<PropertyAttribute>>> typeOrderedPropertyAttributes = new Dictionary<Type, Dictionary<string, List<PropertyAttribute>>>();

        public static bool GetAttrib<T>(Type classType, out T attribOut) where T : Attribute {
            object[] attribs = classType.GetCustomAttributes(typeof(T), false);
            return GetAttrib(attribs, out attribOut);
        }

        public static bool GetAttrib<T>(object[] attribs, out T attribOut) where T : Attribute {
            for (int i = 0; i < attribs.Length; i++) {
                if (attribs[i] is T) {
                    attribOut = attribs[i] as T;
                    return true;
                }
            }
            attribOut = null;
            return false;
        }

        public static bool GetAttrib<T>(Type classType, string fieldName, out T attribOut) where T : Attribute {
            // 如果第一轮找不到字段，那它很可能是基类中的私有字段。
            FieldInfo field = classType.GetFieldInfo(fieldName);
            // 这种情况按理不应发生。
            if (field == null) {
                Debug.LogWarning("Field " + fieldName + " couldnt be found");
                attribOut = null;
                return false;
            }
            object[] attribs = field.GetCustomAttributes(typeof(T), true);
            return GetAttrib(attribs, out attribOut);
        }

        public static bool HasAttrib<T>(object[] attribs) where T : Attribute {
            for (int i = 0; i < attribs.Length; i++) {
                if (attribs[i].GetType() == typeof(T)) {
                    return true;
                }
            }
            return false;
        }

        public static bool GetCachedAttrib<T>(Type classType, string fieldName, out T attribOut) where T : Attribute {
            Dictionary<string, Dictionary<Type, Attribute>> typeFields;
            if (!typeAttributes.TryGetValue(classType, out typeFields)) {
                typeFields = new Dictionary<string, Dictionary<Type, Attribute>>();
                typeAttributes.Add(classType, typeFields);
            }

            Dictionary<Type, Attribute> typeTypes;
            if (!typeFields.TryGetValue(fieldName, out typeTypes)) {
                typeTypes = new Dictionary<Type, Attribute>();
                typeFields.Add(fieldName, typeTypes);
            }

            Attribute attr;
            if (!typeTypes.TryGetValue(typeof(T), out attr)) {
                if (GetAttrib<T>(classType, fieldName, out attribOut)) {
                    typeTypes.Add(typeof(T), attribOut);
                    return true;
                } else typeTypes.Add(typeof(T), null);
            }

            if (attr == null) {
                attribOut = null;
                return false;
            }

            attribOut = attr as T;
            return true;
        }

        /// <summary>
        /// 取字段上全部 PropertyAttribute 的有序缓存列表（按 Unity 的绘制顺序反转存储）；
        /// 首次访问某字段时反射收集并缓存，之后复用。
        /// </summary>
        public static List<PropertyAttribute> GetCachedPropertyAttribs(Type classType, string fieldName) {
            Dictionary<string, List<PropertyAttribute>> typeFields;
            if (!typeOrderedPropertyAttributes.TryGetValue(classType, out typeFields)) {
                typeFields = new Dictionary<string, List<PropertyAttribute>>();
                typeOrderedPropertyAttributes.Add(classType, typeFields);
            }

            List<PropertyAttribute> typeAttributes;
            if (!typeFields.TryGetValue(fieldName, out typeAttributes)) {
                FieldInfo field = classType.GetFieldInfo(fieldName);
                object[] attribs = field.GetCustomAttributes(typeof(PropertyAttribute), true);
                typeAttributes = attribs.Cast<PropertyAttribute>().Reverse().ToList(); //Unity 按相反顺序绘制
                typeFields.Add(fieldName, typeAttributes);
            }

            return typeAttributes;
        }

        /// <summary> 当前系统是否为 macOS（用于区分重命名/删除快捷键） </summary>
        public static bool IsMac() {
            return SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX;
        }

        /// <summary> 判断 from 类型能否赋值/转换为 to 类型 </summary>
        public static bool IsCastableTo(this Type from, Type to) {
            if (to.IsAssignableFrom(from)) return true;
            var methods = from.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(
                    m => m.ReturnType == to &&
                    (m.Name == "op_Implicit" ||
                        m.Name == "op_Explicit")
                );
            return methods.Count() > 0;
        }

        /// <summary>
        /// 查找值类型与指定类型兼容的端口。
        /// </summary>
        /// <param name="nodeType">要搜索的节点类型</param>
        /// <param name="compatibleType">要匹配的兼容类型</param>
        /// <param name="direction"></param>
        /// <returns>True if NodeType has some port with value type compatible</returns>
        public static bool HasCompatiblePortType(Type nodeType, Type compatibleType, XNode.NodePort.IO direction = XNode.NodePort.IO.Input) {
            Type findType = typeof(XNode.Node.InputAttribute);
            if (direction == XNode.NodePort.IO.Output)
                findType = typeof(XNode.Node.OutputAttribute);

            // 遍历节点类型的全部字段，只保留带端口特性的字段，
            // 由此得知各端口的值类型并检查是否存在兼容类型
            foreach (FieldInfo f in XNode.NodeDataCache.GetNodeFields(nodeType)) {
                var portAttribute = f.GetCustomAttributes(findType, false).FirstOrDefault();
                if (portAttribute != null) {
                    if (IsCastableTo(f.FieldType, compatibleType)) {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 只保留端口值类型与指定类型兼容的节点类型。
        /// </summary>
        /// <param name="nodeTypes">List with all nodes type to filter</param>
        /// <param name="compatibleType">Compatible Type to Filter</param>
        /// <returns>Return Only Node Types with ports compatible, or an empty list</returns>
        public static List<Type> GetCompatibleNodesTypes(Type[] nodeTypes, Type compatibleType, XNode.NodePort.IO direction = XNode.NodePort.IO.Input) {
            //Result List
            List<Type> filteredTypes = new List<Type>();

            // 参数无效时返回空列表
            if (nodeTypes == null) { return filteredTypes; }
            if (compatibleType == null) { return filteredTypes; }

            // 逐个类型检查兼容性
            foreach (Type findType in nodeTypes) {
                if (HasCompatiblePortType(findType, compatibleType, direction)) {
                    filteredTypes.Add(findType);
                }
            }

            return filteredTypes;
        }


        /// <summary> 返回美化后的类型名 </summary>
        public static string PrettyName(this Type type) {
            if (type == null) return "null";
            if (type == typeof(System.Object)) return "object";
            if (type == typeof(float)) return "float";
            else if (type == typeof(int)) return "int";
            else if (type == typeof(long)) return "long";
            else if (type == typeof(double)) return "double";
            else if (type == typeof(string)) return "string";
            else if (type == typeof(bool)) return "bool";
            else if (type.IsGenericType) {
                string s = "";
                Type genericType = type.GetGenericTypeDefinition();
                if (genericType == typeof(List<>)) s = "List";
                else s = type.GetGenericTypeDefinition().ToString();

                Type[] types = type.GetGenericArguments();
                string[] stypes = new string[types.Length];
                for (int i = 0; i < types.Length; i++) {
                    stypes[i] = types[i].PrettyName();
                }
                return s + "<" + string.Join(", ", stypes) + ">";
            } else if (type.IsArray) {
                string rank = "";
                for (int i = 1; i < type.GetArrayRank(); i++) {
                    rank += ",";
                }
                Type elementType = type.GetElementType();
                if (!elementType.IsArray) return elementType.PrettyName() + "[" + rank + "]";
                else {
                    string s = elementType.PrettyName();
                    int i = s.IndexOf('[');
                    return s.Substring(0, i) + "[" + rank + "]" + s.Substring(i);
                }
            } else return type.ToString();
        }

        /// <summary> 返回节点类型的默认名。 </summary>
        public static string NodeDefaultName(Type type) {
            string typeName = type.Name;
            // 自动去掉冗余的 'Node' 后缀
            if (typeName.EndsWith("Node")) typeName = typeName.Substring(0, typeName.LastIndexOf("Node"));
            typeName = UnityEditor.ObjectNames.NicifyVariableName(typeName);
            return typeName;
        }

        /// <summary> 返回节点类型的默认创建菜单路径。 </summary>
        public static string NodeDefaultPath(Type type) {
            string typePath = type.ToString().Replace('.', '/');
            // 自动去掉冗余的 'Node' 后缀
            if (typePath.EndsWith("Node")) typePath = typePath.Substring(0, typePath.LastIndexOf("Node"));
            typePath = UnityEditor.ObjectNames.NicifyVariableName(typePath);
            return typePath;
        }

        /// <summary>按模板创建新的 C# 类文件。</summary>
        [MenuItem("Assets/Create/xNode/Node C# Script", false, 89)]
        private static void CreateNode() {
            string[] guids = AssetDatabase.FindAssets("xNode_NodeTemplate.cs");
            if (guids.Length == 0) {
                Debug.LogWarning("xNode_NodeTemplate.cs.txt not found in asset database");
                return;
            }
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            CreateFromTemplate(
                "NewNode.cs",
                path
            );
        }

        /// <summary>按模板创建新的 C# 图类文件。</summary>
        [MenuItem("Assets/Create/xNode/NodeGraph C# Script", false, 89)]
        private static void CreateGraph() {
            string[] guids = AssetDatabase.FindAssets("xNode_NodeGraphTemplate.cs");
            if (guids.Length == 0) {
                Debug.LogWarning("xNode_NodeGraphTemplate.cs.txt not found in asset database");
                return;
            }
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            CreateFromTemplate(
                "NewNodeGraph.cs",
                path
            );
        }

        /// <summary> 启动项目窗口内置的"输入文件名"流程，按模板创建脚本；Unity 6 走 EntityId 版回调 </summary>
        public static void CreateFromTemplate(string initialName, string templatePath) {
#if UNITY_6000_5_OR_NEWER
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                default(EntityId),
                ScriptableObject.CreateInstance<DoCreateCodeFile>(),
                initialName,
                scriptIcon,
                templatePath
            );
#else
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                ScriptableObject.CreateInstance<DoCreateCodeFile>(),
                initialName,
                scriptIcon,
                templatePath
            );
#endif
        }

#if UNITY_6000_5_OR_NEWER
        /// 继承 AssetCreationEndAction，需覆写其 Action
        public class DoCreateCodeFile : UnityEditor.ProjectWindowCallback.AssetCreationEndAction {
            /// <summary> 用户确认文件名后执行：按模板写出脚本并选中新资产 </summary>
            public override void Action(EntityId entityId, string pathName, string resourceFile) {
                Object o = CreateScript(pathName, resourceFile);
                ProjectWindowUtil.ShowCreatedAsset(o);
            }
        }
#else
        /// 继承 EndNameAction，需覆写其 Action
        public class DoCreateCodeFile : UnityEditor.ProjectWindowCallback.EndNameEditAction {
            /// <summary> 用户确认文件名后执行：按模板写出脚本并选中新资产 </summary>
            public override void Action(int instanceId, string pathName, string resourceFile) {
                Object o = CreateScript(pathName, resourceFile);
                ProjectWindowUtil.ShowCreatedAsset(o);
            }
        }
#endif

        /// <summary>按模板路径创建脚本。</summary>
        internal static UnityEngine.Object CreateScript(string pathName, string templatePath) {
            string className = Path.GetFileNameWithoutExtension(pathName).Replace(" ", string.Empty);
            string templateText = string.Empty;

            UTF8Encoding encoding = new UTF8Encoding(true, false);

            if (File.Exists(templatePath)) {
                // 读取模板
                StreamReader reader = new StreamReader(templatePath);
                templateText = reader.ReadToEnd();
                reader.Close();

                templateText = templateText.Replace("#SCRIPTNAME#", className);
                templateText = templateText.Replace("#NOTRIM#", string.Empty);
                // 需要更多占位符就继续追加 Replace
                // 例如: templateText = templateText.Replace("#NEWTAG#", "MyText");

                // 写出文件
                StreamWriter writer = new StreamWriter(Path.GetFullPath(pathName), false, encoding);
                writer.Write(templateText);
                writer.Close();

                AssetDatabase.ImportAsset(pathName);
                return AssetDatabase.LoadAssetAtPath(pathName, typeof(Object));
            } else {
                Debug.LogError(string.Format("The template file was not found: {0}", templatePath));
                return null;
            }
        }
    }
}
