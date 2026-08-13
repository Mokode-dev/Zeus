namespace Zeus;

/// <summary>
/// 自定义帧尾校验算法。校验覆盖「长度域 + 载荷」，不含帧头与校验字节本身。
/// </summary>
public enum FrameChecksumKind
{
    /// <summary>不附加校验。</summary>
    None = 0,

    /// <summary>单字节异或。</summary>
    Xor8 = 1,

    /// <summary>单字节累加（溢出截断）。</summary>
    Sum8 = 2,

    /// <summary>Modbus 多项式的 CRC-16，低字节在前。</summary>
    Crc16Modbus = 3
}
