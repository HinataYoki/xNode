using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace XNodeEditor {
    /// <summary> xNode 专用的 <see cref="EditorGUILayout"/> 扩展 </summary>
    public static class NodeEditorGUILayout {

        private static readonly Dictionary<UnityEngine.Object, Dictionary<string, ReorderableList>> reorderableListCache = new Dictionary<UnityEngine.Object, Dictionary<string, ReorderableList>>();
        private static int reorderableListIndex = -1;

        // GUILayoutOption 只是无状态参数容器，复用同一个实例避免每端口每帧分配
        private static GUILayoutOption s_minWidth;
        private static GUILayoutOption[] s_minWidthOptions;
        private static GUILayoutOption MinWidth30 {
            get { return s_minWidth ?? (s_minWidth = GUILayout.MinWidth(30)); }
        }
        private static GUILayoutOption[] MinWidth30Options {
            get { return s_minWidthOptions ?? (s_minWidthOptions = new GUILayoutOption[] { MinWidth30 }); }
        }

        // 端口标签缓存：(端口, 提示文本) -> GUIContent。标签文本由字段名与特性决定，运行期不变；
        // 消除每帧 new GUIContent + ObjectNames.NicifyVariableName 的重复开销
        private static readonly Dictionary<(XNode.NodePort, string), GUIContent> portLabelCache = new Dictionary<(XNode.NodePort, string), GUIContent>();

        /// <summary> 取端口标签 GUIContent（带缓存）；标签文本即字段名的 Nicify 形式，与 property.displayName 等价 </summary>
        private static GUIContent GetPortLabel(XNode.NodePort port, string tooltip) {
            if (portLabelCache.Count > 256) CleanupPortLabelCache();
            string keyTooltip = tooltip ?? "";
            GUIContent content;
            if (!portLabelCache.TryGetValue((port, keyTooltip), out content)) {
                content = new GUIContent(ObjectNames.NicifyVariableName(port.fieldName), keyTooltip);
                portLabelCache.Add((port, keyTooltip), content);
            }
            return content;
        }

        /// <summary> 清理所属节点已销毁的标签缓存条目 </summary>
        private static void CleanupPortLabelCache() {
            List<(XNode.NodePort, string)> dead = null;
            foreach (var pair in portLabelCache) {
                if (pair.Key.Item1 == null || pair.Key.Item1.node == null) {
                    if (dead == null) dead = new List<(XNode.NodePort, string)>();
                    dead.Add(pair.Key);
                }
            }
            if (dead != null) {
                for (int i = 0; i < dead.Count; i++) portLabelCache.Remove(dead[i]);
            }
        }

        /// <summary> 为序列化属性绘制字段，自动在对应位置显示节点端口 </summary>
        public static void PropertyField(SerializedProperty property, bool includeChildren = true, params GUILayoutOption[] options) {
            PropertyField(property, (GUIContent)null, includeChildren, options);
        }

        /// <summary> 为序列化属性绘制字段，自动在对应位置显示节点端口 </summary>
        public static void PropertyField(SerializedProperty property, GUIContent label, bool includeChildren = true, params GUILayoutOption[] options) {
            if (property == null) throw new NullReferenceException();
            XNode.Node node = property.serializedObject.targetObject as XNode.Node;
            XNode.NodePort port = node.GetPort(property.name);
            PropertyField(property, label, port, includeChildren);
        }

        /// <summary> 为序列化属性绘制字段，手动指定端口 </summary>
        public static void PropertyField(SerializedProperty property, XNode.NodePort port, bool includeChildren = true, params GUILayoutOption[] options) {
            PropertyField(property, null, port, includeChildren, options);
        }

        /// <summary> 为序列化属性绘制字段，手动指定端口 </summary>
        public static void PropertyField(SerializedProperty property, GUIContent label, XNode.NodePort port, bool includeChildren = true, params GUILayoutOption[] options) {
            if (property == null) throw new NullReferenceException();

            // 非端口属性按普通字段绘制
            if (port == null) EditorGUILayout.PropertyField(property, label, includeChildren, MinWidth30Options);
            else {
                Rect rect = new Rect();

                List<PropertyAttribute> propertyAttributes = NodeEditorUtilities.GetCachedPropertyAttribs(port.node.GetType(), property.name);

                // 输入端口：普通字段 + 左侧端口手柄
                if (port.direction == XNode.NodePort.IO.Input) {
                    // 从 [Input] 特性取显示设置
                    XNode.Node.ShowBackingValue showBacking = XNode.Node.ShowBackingValue.Unconnected;
                    XNode.Node.InputAttribute inputAttribute;
                    bool dynamicPortList = false;
                    if (NodeEditorUtilities.GetCachedAttrib(port.node.GetType(), property.name, out inputAttribute)) {
                        dynamicPortList = inputAttribute.dynamicPortList;
                        showBacking = inputAttribute.backingValue;
                    }

                    bool usePropertyAttributes = dynamicPortList ||
                        showBacking == XNode.Node.ShowBackingValue.Never ||
                        (showBacking == XNode.Node.ShowBackingValue.Unconnected && port.IsConnected);

                    float spacePadding = 0;
                    string tooltip = null;
                    DrawPropertyDecorators(propertyAttributes, usePropertyAttributes, ref spacePadding, ref tooltip);

                    if (dynamicPortList) {
                        Type type = GetType(property);
                        XNode.Node.ConnectionType connectionType = inputAttribute != null ? inputAttribute.connectionType : XNode.Node.ConnectionType.Multiple;
                        DynamicPortList(property.name, type, property.serializedObject, port.direction, connectionType);
                        return;
                    }
                    switch (showBacking) {
                        case XNode.Node.ShowBackingValue.Unconnected:
                            // 已连接时显示只读标签，未连接时显示可编辑字段
                            if (port.IsConnected) EditorGUILayout.LabelField(label != null ? label : GetPortLabel(port, tooltip));
                            else EditorGUILayout.PropertyField(property, label, includeChildren, MinWidth30Options);
                            break;
                        case XNode.Node.ShowBackingValue.Never:
                            // 只显示标签
                            EditorGUILayout.LabelField(label != null ? label : GetPortLabel(port, tooltip));
                            break;
                        case XNode.Node.ShowBackingValue.Always:
                            // 始终显示可编辑字段
                            EditorGUILayout.PropertyField(property, label, includeChildren, MinWidth30Options);
                            break;
                    }

                    rect = GUILayoutUtility.GetLastRect();
                    float paddingLeft = NodeEditorWindow.current.graphEditor.GetPortStyle(port).padding.left;
                    rect.position = rect.position - new Vector2(16 + paddingLeft, -spacePadding);
                }
                // 输出端口：文本标签 + 右侧端口手柄
                else if (port.direction == XNode.NodePort.IO.Output) {
                    // 从 [Output] 特性取显示设置
                    XNode.Node.ShowBackingValue showBacking = XNode.Node.ShowBackingValue.Unconnected;
                    XNode.Node.OutputAttribute outputAttribute;
                    bool dynamicPortList = false;
                    if (NodeEditorUtilities.GetCachedAttrib(port.node.GetType(), property.name, out outputAttribute)) {
                        dynamicPortList = outputAttribute.dynamicPortList;
                        showBacking = outputAttribute.backingValue;
                    }

                    bool usePropertyAttributes = dynamicPortList ||
                        showBacking == XNode.Node.ShowBackingValue.Never ||
                        (showBacking == XNode.Node.ShowBackingValue.Unconnected && port.IsConnected);

                    float spacePadding = 0;
                    string tooltip = null;
                    DrawPropertyDecorators(propertyAttributes, usePropertyAttributes, ref spacePadding, ref tooltip);

                    if (dynamicPortList) {
                        Type type = GetType(property);
                        XNode.Node.ConnectionType connectionType = outputAttribute != null ? outputAttribute.connectionType : XNode.Node.ConnectionType.Multiple;
                        DynamicPortList(property.name, type, property.serializedObject, port.direction, connectionType);
                        return;
                    }
                    switch (showBacking) {
                        case XNode.Node.ShowBackingValue.Unconnected:
                            // 已连接时显示右对齐只读标签，未连接时显示可编辑字段
                            if (port.IsConnected) EditorGUILayout.LabelField(label != null ? label : GetPortLabel(port, tooltip), NodeEditorResources.OutputPort, MinWidth30Options);
                            else EditorGUILayout.PropertyField(property, label, includeChildren, MinWidth30Options);
                            break;
                        case XNode.Node.ShowBackingValue.Never:
                            // 右对齐只读标签
                            EditorGUILayout.LabelField(label != null ? label : GetPortLabel(port, tooltip), NodeEditorResources.OutputPort, MinWidth30Options);
                            break;
                        case XNode.Node.ShowBackingValue.Always:
                            // 始终显示可编辑字段
                            EditorGUILayout.PropertyField(property, label, includeChildren, MinWidth30Options);
                            break;
                    }

                    rect = GUILayoutUtility.GetLastRect();
                    rect.width += NodeEditorWindow.current.graphEditor.GetPortStyle(port).padding.right;
                    rect.position = rect.position + new Vector2(rect.width, spacePadding);
                }

                rect.size = new Vector2(16, 16);

                Color backgroundColor = NodeEditorWindow.current.graphEditor.GetPortBackgroundColor(port);
                Color col = NodeEditorWindow.current.graphEditor.GetPortColor(port);
                GUIStyle portStyle = NodeEditorWindow.current.graphEditor.GetPortStyle(port);
                DrawPortHandle(rect, backgroundColor, col, portStyle.normal.background, portStyle.active.background);

                // 登记端口手柄位置
                Vector2 portPos = rect.center;
                NodeEditorWindow.current.portPositions[port] = portPos;
            }
        }

        /// <summary> 绘制属性上的 Space/Header 装饰器并收集 Tooltip；未走装饰器路径时折算为纵向留白 </summary>
        private static void DrawPropertyDecorators(List<PropertyAttribute> propertyAttributes, bool usePropertyAttributes, ref float spacePadding, ref string tooltip) {
            foreach (var attr in propertyAttributes) {
                if (attr is SpaceAttribute) {
                    if (usePropertyAttributes) GUILayout.Space((attr as SpaceAttribute).height);
                    else spacePadding += (attr as SpaceAttribute).height;
                } else if (attr is HeaderAttribute) {
                    if (usePropertyAttributes) {
                        // 布局会在 rect 之后追加 standardVerticalSpacing，因此先减去
                        Rect position = GUILayoutUtility.GetRect(0, (EditorGUIUtility.singleLineHeight * 1.5f) - EditorGUIUtility.standardVerticalSpacing);
                        position.yMin += EditorGUIUtility.singleLineHeight * 0.5f;
                        position = EditorGUI.IndentedRect(position);
                        GUI.Label(position, (attr as HeaderAttribute).header, EditorStyles.boldLabel);
                    } else spacePadding += EditorGUIUtility.singleLineHeight * 1.5f;
                } else if (attr is TooltipAttribute) {
                    tooltip = (attr as TooltipAttribute).tooltip;
                }
            }
        }

        /// <summary> 反射取序列化属性对应字段的声明类型，用作动态端口列表元素的端口值类型 </summary>
        private static System.Type GetType(SerializedProperty property) {
            System.Type parentType = property.serializedObject.targetObject.GetType();
            System.Reflection.FieldInfo fi = parentType.GetFieldInfo(property.name);
            return fi.FieldType;
        }

        /// <summary> 绘制一个简单的端口字段 </summary>
        public static void PortField(XNode.NodePort port, params GUILayoutOption[] options) {
            PortField(null, port, options);
        }

        /// <summary> 绘制一个带标签的简单端口字段 </summary>
        public static void PortField(GUIContent label, XNode.NodePort port, params GUILayoutOption[] options) {
            if (port == null) return;
            if (options == null) options = MinWidth30Options;
            Vector2 position = Vector3.zero;
            GUIContent content = label != null ? label : GetPortLabel(port, null);

            // 输入端口：标签 + 左侧手柄
            if (port.direction == XNode.NodePort.IO.Input) {
                EditorGUILayout.LabelField(content, options);

                Rect rect = GUILayoutUtility.GetLastRect();
                float paddingLeft = NodeEditorWindow.current.graphEditor.GetPortStyle(port).padding.left;
                position = rect.position - new Vector2(16 + paddingLeft, 0);
            }
            // 输出端口：右对齐标签 + 右侧手柄
            else if (port.direction == XNode.NodePort.IO.Output) {
                EditorGUILayout.LabelField(content, NodeEditorResources.OutputPort, options);

                Rect rect = GUILayoutUtility.GetLastRect();
                rect.width += NodeEditorWindow.current.graphEditor.GetPortStyle(port).padding.right;
                position = rect.position + new Vector2(rect.width, 0);
            }
            PortField(position, port);
        }

        /// <summary> 在指定相对位置绘制一个端口字段 </summary>
        public static void PortField(Vector2 position, XNode.NodePort port) {
            if (port == null) return;

            Rect rect = new Rect(position, new Vector2(16, 16));

            Color backgroundColor = NodeEditorWindow.current.graphEditor.GetPortBackgroundColor(port);
            Color col = NodeEditorWindow.current.graphEditor.GetPortColor(port);
            GUIStyle portStyle = NodeEditorWindow.current.graphEditor.GetPortStyle(port);

            DrawPortHandle(rect, backgroundColor, col, portStyle.normal.background, portStyle.active.background);

            // 登记端口手柄位置
            Vector2 portPos = rect.center;
            NodeEditorWindow.current.portPositions[port] = portPos;
        }

        /// <summary> 把端口手柄添加到上一个布局元素上 </summary>
        public static void AddPortField(XNode.NodePort port) {
            if (port == null) return;
            Rect rect = new Rect();

            // 输入端口：手柄放在上一个元素左侧
            if (port.direction == XNode.NodePort.IO.Input) {
                rect = GUILayoutUtility.GetLastRect();
                float paddingLeft = NodeEditorWindow.current.graphEditor.GetPortStyle(port).padding.left;
                rect.position = rect.position - new Vector2(16 + paddingLeft, 0);
            }
            // 输出端口：手柄放在上一个元素右侧
            else if (port.direction == XNode.NodePort.IO.Output) {
                rect = GUILayoutUtility.GetLastRect();
                rect.width += NodeEditorWindow.current.graphEditor.GetPortStyle(port).padding.right;
                rect.position = rect.position + new Vector2(rect.width, 0);
            }

            rect.size = new Vector2(16, 16);

            Color backgroundColor = NodeEditorWindow.current.graphEditor.GetPortBackgroundColor(port);
            Color col = NodeEditorWindow.current.graphEditor.GetPortColor(port);
            GUIStyle portStyle = NodeEditorWindow.current.graphEditor.GetPortStyle(port);

            DrawPortHandle(rect, backgroundColor, col, portStyle.normal.background, portStyle.active.background);

            // 登记端口手柄位置
            Vector2 portPos = rect.center;
            NodeEditorWindow.current.portPositions[port] = portPos;
        }

        /// <summary> 在同一行绘制一对输入/输出端口 </summary>
        public static void PortPair(XNode.NodePort input, XNode.NodePort output) {
            GUILayout.BeginHorizontal();
            NodeEditorGUILayout.PortField(input, GUILayout.MinWidth(0));
            NodeEditorGUILayout.PortField(output, GUILayout.MinWidth(0));
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制端口手柄圆点
        /// </summary>
        /// <param name="rect">位置与尺寸</param>
        /// <param name="backgroundColor">端口背景纹理颜色，通常用作描边</param>
        /// <param name="typeColor">端口圆点颜色</param>
        /// <param name="border">圆点描边纹理</param>
        /// <param name="dot">圆点主体纹理</param>
        public static void DrawPortHandle(Rect rect, Color backgroundColor, Color typeColor, Texture2D border, Texture2D dot) {
            Color col = GUI.color;
            GUI.color = backgroundColor;
            GUI.DrawTexture(rect, border);
            GUI.color = typeColor;
            GUI.DrawTexture(rect, dot);
            GUI.color = col;
        }

        /// <summary> 该端口是否属于某个动态端口列表；对列表键做前缀匹配，零字符串分配 </summary>
        public static bool IsDynamicPortListPort(XNode.NodePort port) {
            Dictionary<string, ReorderableList> cache;
            if (!reorderableListCache.TryGetValue(port.node, out cache)) return false;

            string fieldName = port.fieldName;
            foreach (var pair in cache) {
                string key = pair.Key;
                // 字段名须形如 "<key> <序号>"：前缀命中 + 一个空格 + 纯数字，与 Split 版语义对齐
                if (fieldName.Length <= key.Length || fieldName[key.Length] != ' ' || !fieldName.StartsWith(key, System.StringComparison.Ordinal)) continue;
                if (fieldName.IndexOf(' ', key.Length + 1) != -1) continue;
                bool valid = fieldName.Length > key.Length + 1;
                for (int c = key.Length + 1; valid && c < fieldName.Length; c++) {
                    if (fieldName[c] < '0' || fieldName[c] > '9') valid = false;
                }
                if (valid) return true;
            }
            return false;
        }

        /// <summary> 清理键已销毁的列表缓存条目；节点删除后 Unity 假 null 键仍留托管引用 </summary>
        private static void CleanupDestroyedListCache() {
            List<UnityEngine.Object> deadKeys = null;
            foreach (var pair in reorderableListCache) {
                if (pair.Key == null) {
                    if (deadKeys == null) deadKeys = new List<UnityEngine.Object>();
                    deadKeys.Add(pair.Key);
                }
            }
            if (deadKeys != null) {
                for (int i = 0; i < deadKeys.Count; i++) reorderableListCache.Remove(deadKeys[i]);
            }
        }

        /// <summary> 绘制动态端口的可编辑列表；端口按 "[字段名] [序号]" 命名 </summary>
        /// <param name="fieldName">为其提供可编辑值的字段名</param>
        /// <param name="type">新增动态端口的值类型</param>
        /// <param name="serializedObject">节点的 serializedObject</param>
        /// <param name="connectionType">新增端口的连接类型</param>
        /// <param name="onCreation">列表创建后的回调，用于自定义 ReorderableList</param>
        public static void DynamicPortList(string fieldName, Type type, SerializedObject serializedObject, XNode.NodePort.IO io, XNode.Node.ConnectionType connectionType = XNode.Node.ConnectionType.Multiple, XNode.Node.TypeConstraint typeConstraint = XNode.Node.TypeConstraint.None, Action<ReorderableList> onCreation = null) {
            XNode.Node node = serializedObject.targetObject as XNode.Node;

            CleanupDestroyedListCache();

            List<XNode.NodePort> dynamicPorts = CollectIndexedDynamicPorts(node, fieldName);

            node.UpdatePorts();

            ReorderableList list = null;
            Dictionary<string, ReorderableList> rlc;
            if (reorderableListCache.TryGetValue(serializedObject.targetObject, out rlc)) {
                if (!rlc.TryGetValue(fieldName, out list)) list = null;
            }
            // 该列表没有缓存时新建
            if (list == null) {
                SerializedProperty arrayData = serializedObject.FindProperty(fieldName);
                list = CreateReorderableList(fieldName, dynamicPorts, arrayData, type, serializedObject, io, connectionType, typeConstraint, onCreation);
                if (reorderableListCache.TryGetValue(serializedObject.targetObject, out rlc)) rlc.Add(fieldName, list);
                else reorderableListCache.Add(serializedObject.targetObject, new Dictionary<string, ReorderableList>() { { fieldName, list } });
            }
            list.list = dynamicPorts;
            list.DoLayoutList();

        }

        // 收集过程中的复用缓冲：序号缓冲直接复用，端口快照遍历结束即失效
        private static readonly List<XNode.NodePort> s_dynamicPortBuffer = new List<XNode.NodePort>(16);
        private static readonly List<int> s_indexBuffer = new List<int>(16);

        /// <summary> 按序号升序收集属于指定动态端口列表的端口；结果列表被 ReorderableList 持有，故每次新建 </summary>
        private static List<XNode.NodePort> CollectIndexedDynamicPorts(XNode.Node node, string fieldName) {
            node.GetDynamicPorts(s_dynamicPortBuffer);

            List<XNode.NodePort> result = new List<XNode.NodePort>(s_dynamicPortBuffer.Count);
            List<int> indices = s_indexBuffer;
            indices.Clear();
            for (int i = 0; i < s_dynamicPortBuffer.Count; i++) {
                string name = s_dynamicPortBuffer[i].fieldName;
                // 名字形如 "fieldName N" 且 N 可解析才算列表端口；尾缀手工解析避免 Substring 分配
                if (name.Length <= fieldName.Length + 1 || name[fieldName.Length] != ' ') continue;
                int index = 0;
                bool valid = name.Length > fieldName.Length + 1;
                for (int c = fieldName.Length + 1; valid && c < name.Length; c++) {
                    char ch = name[c];
                    if (ch < '0' || ch > '9') { valid = false; break; }
                    index = index * 10 + (ch - '0');
                }
                if (!valid) continue;
                // 按序号插入到有序位置（列表端口数少，线性插入足够）
                int insertAt = 0;
                while (insertAt < indices.Count && indices[insertAt] < index) insertAt++;
                indices.Insert(insertAt, index);
                result.Insert(insertAt, s_dynamicPortBuffer[i]);
            }
            return result;
        }

        /// <summary>
        /// 构建动态端口列表的 ReorderableList：每个元素旁绘制端口手柄；重排序时相邻端口
        /// 逐个交换连接并同步锚点缓存；增删端口时按序号补齐/收缩动态端口与数组数据。
        /// </summary>
        private static ReorderableList CreateReorderableList(string fieldName, List<XNode.NodePort> dynamicPorts, SerializedProperty arrayData, Type type, SerializedObject serializedObject, XNode.NodePort.IO io, XNode.Node.ConnectionType connectionType, XNode.Node.TypeConstraint typeConstraint, Action<ReorderableList> onCreation) {
            bool hasArrayData = arrayData != null && arrayData.isArray;
            XNode.Node node = serializedObject.targetObject as XNode.Node;
            ReorderableList list = new ReorderableList(dynamicPorts, null, true, true, true, true);
            string label = arrayData != null ? arrayData.displayName : ObjectNames.NicifyVariableName(fieldName);

            list.drawElementCallback =
                (Rect rect, int index, bool isActive, bool isFocused) => {
                    XNode.NodePort port = node.GetPort(fieldName + " " + index);
                    if (hasArrayData && arrayData.propertyType != SerializedPropertyType.String) {
                        // 数组数据越界时提示而非崩溃
                        if (arrayData.arraySize <= index) {
                            EditorGUI.LabelField(rect, "Array[" + index + "] data out of range");
                            return;
                        }
                        SerializedProperty itemData = arrayData.GetArrayElementAtIndex(index);
                        EditorGUI.PropertyField(rect, itemData, true);
                    } else EditorGUI.LabelField(rect, port != null ? port.fieldName : "");
                    if (port != null) {
                        Vector2 pos = rect.position + (port.IsOutput ? new Vector2(rect.width + 6, 0) : new Vector2(-36, 0));
                        NodeEditorGUILayout.PortField(pos, port);
                    }
                };
            list.elementHeightCallback =
                (int index) => {
                    if (hasArrayData) {
                        if (arrayData.arraySize <= index) return EditorGUIUtility.singleLineHeight;
                        SerializedProperty itemData = arrayData.GetArrayElementAtIndex(index);
                        return EditorGUI.GetPropertyHeight(itemData);
                    } else return EditorGUIUtility.singleLineHeight;
                };
            list.drawHeaderCallback =
                (Rect rect) => {
                    EditorGUI.LabelField(rect, label);
                };
            list.onSelectCallback =
                (ReorderableList rl) => {
                    reorderableListIndex = rl.index;
                };
            list.onReorderCallback =
                (ReorderableList rl) => {
                    // 重排序 = 相邻端口逐个交换连接，同时交换锚点缓存避免连线抖动
                    serializedObject.Update();
                    bool hasRect = false;
                    bool hasNewRect = false;
                    Rect rect = Rect.zero;
                    Rect newRect = Rect.zero;
                    // 上移
                    if (rl.index > reorderableListIndex) {
                        for (int i = reorderableListIndex; i < rl.index; ++i) {
                            XNode.NodePort port = node.GetPort(fieldName + " " + i);
                            XNode.NodePort nextPort = node.GetPort(fieldName + " " + (i + 1));
                            port.SwapConnections(nextPort);

                            hasRect = NodeEditorWindow.current.portConnectionPoints.TryGetValue(port, out rect);
                            hasNewRect = NodeEditorWindow.current.portConnectionPoints.TryGetValue(nextPort, out newRect);
                            NodeEditorWindow.current.portConnectionPoints[port] = hasNewRect ? newRect : rect;
                            NodeEditorWindow.current.portConnectionPoints[nextPort] = hasRect ? rect : newRect;
                        }
                    }
                    // 下移
                    else {
                        for (int i = reorderableListIndex; i > rl.index; --i) {
                            XNode.NodePort port = node.GetPort(fieldName + " " + i);
                            XNode.NodePort nextPort = node.GetPort(fieldName + " " + (i - 1));
                            port.SwapConnections(nextPort);

                            hasRect = NodeEditorWindow.current.portConnectionPoints.TryGetValue(port, out rect);
                            hasNewRect = NodeEditorWindow.current.portConnectionPoints.TryGetValue(nextPort, out newRect);
                            NodeEditorWindow.current.portConnectionPoints[port] = hasNewRect ? newRect : rect;
                            NodeEditorWindow.current.portConnectionPoints[nextPort] = hasRect ? rect : newRect;
                        }
                    }
                    // 应用变更
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();

                    // 存在数组数据时同步移动数组元素
                    if (hasArrayData) {
                        arrayData.MoveArrayElement(reorderableListIndex, rl.index);
                    }

                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                    NodeEditorWindow.current.Repaint();
                    EditorApplication.delayCall += NodeEditorWindow.current.Repaint;
                };
            list.onAddCallback =
                (ReorderableList rl) => {
                    // 按序号后缀命名新增端口
                    string newName = fieldName + " 0";
                    int i = 0;
                    while (node.HasPort(newName)) newName = fieldName + " " + (++i);

                    if (io == XNode.NodePort.IO.Output) node.AddDynamicOutput(type, connectionType, XNode.Node.TypeConstraint.None, newName);
                    else node.AddDynamicInput(type, connectionType, typeConstraint, newName);
                    serializedObject.Update();
                    EditorUtility.SetDirty(node);
                    if (hasArrayData) {
                        arrayData.InsertArrayElementAtIndex(arrayData.arraySize);
                    }
                    serializedObject.ApplyModifiedProperties();
                };
            list.onRemoveCallback =
                (ReorderableList rl) => {
                    dynamicPorts = CollectIndexedDynamicPorts(node, fieldName);

                    int index = rl.index;

                    if (dynamicPorts.Count <= index || dynamicPorts[index] == null) {
                        Debug.LogWarning("动态端口索引 " + index + " 无效，已跳过移除");
                    } else {
                        // 清空被移除端口的连接
                        dynamicPorts[index].ClearConnections();
                        // 后续端口依次上移补位
                        for (int k = index + 1; k < dynamicPorts.Count; k++) {
                            for (int j = 0; j < dynamicPorts[k].ConnectionCount; j++) {
                                XNode.NodePort other = dynamicPorts[k].GetConnection(j);
                                dynamicPorts[k].Disconnect(other);
                                dynamicPorts[k - 1].Connect(other);
                            }
                        }
                        // 移除最后一个端口，避免序号错位
                        node.RemoveDynamicPort(dynamicPorts[dynamicPorts.Count - 1].fieldName);
                        serializedObject.Update();
                        EditorUtility.SetDirty(node);
                    }

                    if (hasArrayData && arrayData.propertyType != SerializedPropertyType.String) {
                        if (arrayData.arraySize <= index) {
                            Debug.LogWarning("尝试移除数组索引 " + index + "，但只有 " + arrayData.arraySize + " 个元素，已跳过");
                            return;
                        }
                        arrayData.DeleteArrayElementAtIndex(index);
                        // 数组元素多于动态端口时删掉多余元素（频繁出现请上报）
                        if (dynamicPorts.Count <= arrayData.arraySize) {
                            while (dynamicPorts.Count <= arrayData.arraySize) {
                                arrayData.DeleteArrayElementAtIndex(arrayData.arraySize - 1);
                            }
                            UnityEngine.Debug.LogWarning("数组长度超过动态端口数量，已移除多余元素");
                        }
                        serializedObject.ApplyModifiedProperties();
                        serializedObject.Update();
                    }
                };

            // 数组数据与端口数量不一致时先对齐
            if (hasArrayData) {
                int dynamicPortCount = dynamicPorts.Count;
                while (dynamicPortCount < arrayData.arraySize) {
                    string newName = arrayData.name + " 0";
                    int i = 0;
                    while (node.HasPort(newName)) newName = arrayData.name + " " + (++i);
                    if (io == XNode.NodePort.IO.Output) node.AddDynamicOutput(type, connectionType, typeConstraint, newName);
                    else node.AddDynamicInput(type, connectionType, typeConstraint, newName);
                    EditorUtility.SetDirty(node);
                    dynamicPortCount++;
                }
                while (arrayData.arraySize < dynamicPortCount) {
                    arrayData.InsertArrayElementAtIndex(arrayData.arraySize);
                }
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }
            if (onCreation != null) onCreation(list);
            return list;
        }
    }
}
