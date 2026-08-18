namespace Zeus;

/// <summary>
/// Omron Host Link 会话选项。
/// </summary>
public sealed class HostLinkOptions
{
    /// <summary>单元号，Host Link 帧中的两位十进制站号，默认 0。</summary>
    public byte UnitNumber { get; set; }

    /// <summary>连续两个字转换 32 位工程值时的字序，默认高字在前。</summary>
    public HostLinkWordOrder WordOrder { get; set; } = HostLinkWordOrder.HighWordFirst;
}
