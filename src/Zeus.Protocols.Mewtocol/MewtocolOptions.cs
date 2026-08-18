namespace Zeus;

/// <summary>
/// Panasonic MEWTOCOL-COM 会话选项。
/// </summary>
public sealed class MewtocolOptions
{
    /// <summary>站号，MEWTOCOL 帧中的两位十进制站号，默认 1。</summary>
    public byte StationNumber { get; set; } = 1;

    /// <summary>连续两个字转换 32 位工程值时的字序，默认高字在前。</summary>
    public MewtocolWordOrder WordOrder { get; set; } = MewtocolWordOrder.HighWordFirst;
}
