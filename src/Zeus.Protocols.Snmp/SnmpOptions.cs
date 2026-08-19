namespace Zeus;

/// <summary>SNMP v2c 会话选项。</summary>
public sealed class SnmpOptions
{
    /// <summary>读 community，默认 public。</summary>
    public string Community { get; set; } = "public";

    /// <summary>写 community。省略时沿用 <see cref="Community"/>。</summary>
    public string? WriteCommunity { get; set; }

    /// <summary>请求 ID 初始值，默认 1。</summary>
    public int InitialRequestId { get; set; } = 1;
}
