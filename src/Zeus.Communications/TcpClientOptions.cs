namespace Zeus;

/// <summary>
/// TCP 客户端通道选项。
/// </summary>
public sealed class TcpClientOptions
{
    /// <summary>对端主机名或 IP，默认 <c>127.0.0.1</c>。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>对端端口，默认 502（Modbus TCP）。</summary>
    public int Port { get; set; } = 502;

    /// <summary>连接超时（毫秒）。</summary>
    public int ConnectTimeoutMilliseconds { get; set; } = 3000;
}
