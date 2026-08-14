namespace Zeus;

/// <summary>
/// UDP 服务端通道选项。
/// </summary>
public sealed class UdpServerOptions
{
    /// <summary>本地监听地址，默认 <c>0.0.0.0</c>。</summary>
    public string LocalAddress { get; set; } = "0.0.0.0";

    /// <summary>本地监听端口。为 0 时由操作系统分配临时端口。</summary>
    public int LocalPort { get; set; } = 502;

    /// <summary>接收缓冲区大小（字节）。</summary>
    public int ReceiveBufferSize { get; set; } = 8192;
}
