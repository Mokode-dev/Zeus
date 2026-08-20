namespace Zeus;

/// <summary>
/// TCP 服务端通道选项。
/// </summary>
public sealed class TcpServerOptions
{
    /// <summary>
    /// 本地监听地址。默认 <c>127.0.0.1</c>，避免未配置时把虚拟从站暴露到全部网卡。
    /// 现场需要对外服务时显式设为 <c>0.0.0.0</c>。
    /// </summary>
    public string LocalAddress { get; set; } = "127.0.0.1";

    /// <summary>本地监听端口。为 0 时由操作系统分配临时端口。</summary>
    public int LocalPort { get; set; } = 502;

    /// <summary>监听队列长度。</summary>
    public int Backlog { get; set; } = 100;

    /// <summary>接收缓冲区大小（字节）。</summary>
    public int ReceiveBufferSize { get; set; } = 8192;

    /// <summary>
    /// 同时接受的客户端上限。超过后新连接会被立即断开。
    /// 默认 32，避免未限制接受把进程打满。
    /// </summary>
    public int MaxClients { get; set; } = 32;
}
