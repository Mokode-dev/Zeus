namespace Zeus;

/// <summary>
/// Siemens S7 ISO-on-TCP 会话选项。
/// </summary>
public sealed class S7Options
{
    /// <summary>机架号，默认 0。</summary>
    public byte Rack { get; set; }

    /// <summary>槽号，默认 1。部分 S7-300/400 项目常用 2。</summary>
    public byte Slot { get; set; } = 1;

    /// <summary>本地 TSAP，默认 <c>0x0100</c>。</summary>
    public ushort LocalTsap { get; set; } = 0x0100;

    /// <summary>
    /// 远端 TSAP。为 <c>null</c> 时按 <see cref="Rack"/> 和 <see cref="Slot"/> 计算，格式为 <c>0x0100 + rack * 0x20 + slot</c>。
    /// </summary>
    public ushort? RemoteTsap { get; set; }

    /// <summary>请求协商的 S7 PDU 长度，默认 480 字节。</summary>
    public ushort RequestedPduLength { get; set; } = 480;

    internal ushort EffectiveRemoteTsap => RemoteTsap ?? (ushort)(0x0100 + (Rack * 0x20) + Slot);
}
