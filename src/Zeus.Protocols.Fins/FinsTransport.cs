namespace Zeus;

/// <summary>
/// Omron FINS 在线封装。
/// </summary>
public enum FinsTransport
{
    /// <summary>FINS/UDP，通道通常是 UDP 客户端。</summary>
    Udp = 0,

    /// <summary>FINS/TCP，连接后会执行节点地址握手。</summary>
    Tcp = 1
}
