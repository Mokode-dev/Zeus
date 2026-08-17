namespace Zeus;

/// <summary>
/// Omron FINS 会话选项。UDP 需要显式配置目标节点；TCP 可通过握手自动同步源/目标节点号。
/// </summary>
public sealed class FinsOptions
{
    /// <summary>目标网络号 DNA，默认 0 表示本网络。</summary>
    public byte DestinationNetwork { get; set; }

    /// <summary>目标节点号 DA1。UDP 现场通常设置为 PLC 节点号；TCP 握手后可自动填充。</summary>
    public byte DestinationNode { get; set; }

    /// <summary>目标单元号 DA2，CPU 单元通常为 0。</summary>
    public byte DestinationUnit { get; set; }

    /// <summary>源网络号 SNA，默认 0 表示本网络。</summary>
    public byte SourceNetwork { get; set; }

    /// <summary>源节点号 SA1。UDP 现场通常设置为本机节点号；TCP 握手后可自动填充。</summary>
    public byte SourceNode { get; set; }

    /// <summary>源单元号 SA2，默认 0。</summary>
    public byte SourceUnit { get; set; }

    /// <summary>网关计数 GCT，常用默认值 2。</summary>
    public byte GatewayCount { get; set; } = 2;

    /// <summary>ICF 控制字段。默认 0x80 表示需要响应且不使用网关。</summary>
    public byte InformationControlField { get; set; } = 0x80;

    /// <summary>FINS/TCP 握手时发送的客户端节点号。0 表示请求服务端分配。</summary>
    public byte TcpRequestedClientNode { get; set; }

    /// <summary>FINS/TCP 是否通过节点地址握手自动更新 SourceNode 与 DestinationNode，默认 true。</summary>
    public bool UseTcpNodeAddressHandshake { get; set; } = true;

    /// <summary>连续两个字转换 32 位工程值时的字序，默认高字在前。</summary>
    public FinsWordOrder WordOrder { get; set; } = FinsWordOrder.HighWordFirst;
}
