namespace Zeus;

/// <summary>
/// EtherNet/IP CIP 原子数据类型。
/// </summary>
public enum EtherNetIpDataType : ushort
{
    /// <summary>BOOL，CIP 类型码 0x00C1。</summary>
    Bool = 0x00C1,

    /// <summary>SINT，8 位有符号整数。</summary>
    SInt = 0x00C2,

    /// <summary>INT，16 位有符号整数。</summary>
    Int = 0x00C3,

    /// <summary>DINT，32 位有符号整数。</summary>
    DInt = 0x00C4,

    /// <summary>LINT，64 位有符号整数。</summary>
    LInt = 0x00C5,

    /// <summary>USINT，8 位无符号整数。</summary>
    USInt = 0x00C6,

    /// <summary>UINT，16 位无符号整数。</summary>
    UInt = 0x00C7,

    /// <summary>UDINT，32 位无符号整数。</summary>
    UDInt = 0x00C8,

    /// <summary>ULINT，64 位无符号整数。</summary>
    ULInt = 0x00C9,

    /// <summary>REAL，32 位浮点数。</summary>
    Real = 0x00CA,

    /// <summary>LREAL，64 位浮点数。</summary>
    LReal = 0x00CB
}
