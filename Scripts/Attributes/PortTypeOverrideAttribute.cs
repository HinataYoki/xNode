using System;
/// <summary> 覆盖端口的值类型，使其与序列化字段类型不同 </summary>
/// <remarks> 在动态端口列表中为值-端口对指定不同类型时特别有用 </remarks>
[AttributeUsage(AttributeTargets.Field)]
public class PortTypeOverrideAttribute : Attribute {
    public Type type;
    /// <summary> 覆盖端口的值类型 </summary>
    /// <param name="type">端口值类型</param>
    public PortTypeOverrideAttribute(Type type) {
        this.type = type;
    }
}
