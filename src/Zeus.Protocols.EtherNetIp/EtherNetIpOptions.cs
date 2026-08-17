namespace Zeus;

/// <summary>
/// EtherNet/IP 会话选项。
/// </summary>
public sealed class EtherNetIpOptions
{
    /// <summary>Register Session 协议版本，通常为 1。</summary>
    public ushort ProtocolVersion { get; set; } = 1;

    /// <summary>SendRRData 的超时字段，单位由对端解释；0 表示使用默认。</summary>
    public ushort CpfTimeout { get; set; }
}
