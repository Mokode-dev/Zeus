namespace Zeus;

/// <summary>
/// 长度域编码。长度值等于载荷字节数，不含帧头、长度域自身与校验。
/// </summary>
public enum FrameLengthKind
{
    /// <summary>1 字节，载荷最长 255。</summary>
    UInt8 = 0,

    /// <summary>2 字节小端。</summary>
    UInt16LittleEndian = 1,

    /// <summary>2 字节大端。</summary>
    UInt16BigEndian = 2
}
