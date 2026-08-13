namespace Zeus;

/// <summary>
/// UDP 客户端通道选项。
/// </summary>
public sealed class UdpClientOptions
{
    /// <summary>对端主机名或 IP，默认 <c>127.0.0.1</c>。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>对端端口，默认 502。</summary>
    public int Port { get; set; } = 502;

    /// <summary>本地绑定端口。为 0 时由操作系统分配临时端口。</summary>
    public int LocalPort { get; set; }

    /// <summary>接收缓冲区大小（字节）。</summary>
    public int ReceiveBufferSize { get; set; } = 8192;
}
