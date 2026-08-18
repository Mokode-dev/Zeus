namespace Zeus;

/// <summary>
/// DL/T 645 数据项的解码方式。
/// </summary>
public enum Dlt645DataType
{
    /// <summary>低字节在前的压缩 BCD 数值，按 <c>scale</c> 换算为工程值。</summary>
    Bcd = 0,

    /// <summary>保留原始数据字节，不做数值换算。</summary>
    RawBytes = 1
}
