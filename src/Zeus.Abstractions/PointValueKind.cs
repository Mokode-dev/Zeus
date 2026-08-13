namespace Zeus;

/// <summary>
/// 点表中值的运行时类型。采集循环按此决定如何装箱与转换。
/// </summary>
public enum PointValueKind
{
    /// <summary>16 位无符号整数，对应保持/输入寄存器的原始值。</summary>
    UInt16 = 0,

    /// <summary>布尔，对应线圈或离散输入。</summary>
    Boolean = 1,

    /// <summary>经过换算后的浮点，例如寄存器值乘以 0.1。</summary>
    Double = 2,

    /// <summary>自定义转换结果，调用方自行拆箱。</summary>
    Object = 3
}
