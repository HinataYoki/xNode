using UnityEngine;

/// <summary> 让枚举在节点内以正确位置绘制；不加本特性时枚举下拉框位置会偏移 </summary>
/// <remarks> 标注本特性的枚举因为延迟执行，不会被 EditorGui.ChangeCheck 检测到变更 </remarks>
public class NodeEnumAttribute : PropertyAttribute { }
