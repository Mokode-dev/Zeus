namespace Zeus;

/// <summary>
/// Mitsubishi MC Protocol 帧选项。默认使用 3E Binary。
/// </summary>
public sealed class Mc3EOptions
{
    /// <summary>帧类型，默认 <see cref="McFrameType.Frame3E"/>。</summary>
    public McFrameType FrameType { get; set; } = McFrameType.Frame3E;

    /// <summary>数据编码，默认 <see cref="McDataEncoding.Binary"/>。</summary>
    public McDataEncoding DataEncoding { get; set; } = McDataEncoding.Binary;

    /// <summary>4E 帧序列号，默认 <c>0x0000</c>。3E / 1E 帧忽略此值。</summary>
    public ushort SerialNumber { get; set; }

    /// <summary>网络号，默认 <c>0x00</c>。</summary>
    public byte NetworkNumber { get; set; }

    /// <summary>PC 号，默认 <c>0xFF</c>。</summary>
    public byte PcNumber { get; set; } = 0xFF;

    /// <summary>请求目标模块 I/O 号，默认 <c>0x03FF</c>。</summary>
    public ushort IoNumber { get; set; } = 0x03FF;

    /// <summary>请求目标模块站号，默认 <c>0x00</c>。</summary>
    public byte StationNumber { get; set; }

    /// <summary>监视定时器，单位为 250ms。默认 <c>0x0010</c>。</summary>
    public ushort MonitoringTimer { get; set; } = 0x0010;
}
