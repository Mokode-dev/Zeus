namespace Zeus;

/// <summary>
/// Siemens S7 点的数据类型。
/// </summary>
public enum S7DataType
{
    /// <summary>单个位。</summary>
    Bool = 0,

    /// <summary>8 位无符号整数。</summary>
    Byte = 1,

    /// <summary>16 位无符号整数。</summary>
    Word = 2,

    /// <summary>32 位无符号整数。</summary>
    DWord = 3,

    /// <summary>16 位有符号整数。</summary>
    Int = 4,

    /// <summary>32 位有符号整数。</summary>
    DInt = 5,

    /// <summary>32 位 IEEE 浮点数。</summary>
    Real = 6
}
