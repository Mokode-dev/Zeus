namespace Zeus;

/// <summary>
/// Host Link 点表和高层读写的值类型。
/// </summary>
public enum HostLinkDataType
{
    /// <summary>单个位。通过读写所在字实现。</summary>
    Bit = 0,

    /// <summary>16 位无符号字。</summary>
    Word = 1,

    /// <summary>16 位有符号整数。</summary>
    Int16 = 2,

    /// <summary>32 位无符号整数，占两个连续字。</summary>
    UInt32 = 3,

    /// <summary>32 位有符号整数，占两个连续字。</summary>
    Int32 = 4,

    /// <summary>32 位 IEEE 浮点数，占两个连续字。</summary>
    Real = 5
}
