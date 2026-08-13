namespace Zeus;

/// <summary>
/// 传输层选项校验。构建通道时尽早失败，避免把低层套接字异常暴露给用户。
/// </summary>
internal static class CommunicationOptionGuard
{
    /// <summary>
    /// 校验远端主机名或 IP。
    /// </summary>
    /// <param name="channelName">通道名。</param>
    /// <param name="host">主机名或 IP。</param>
    /// <param name="transport">传输类型。</param>
    /// <returns>去除首尾空白后的主机名。</returns>
    public static string Host(string? host, string channelName, string transport)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ZeusChannelException(
                channelName,
                $"通道 {channelName} 的 {transport} 主机名不能为空。请指定对端主机名或 IP 地址。");
        }

        return host.Trim();
    }

    /// <summary>
    /// 校验远端 TCP/UDP 端口。
    /// </summary>
    /// <param name="channelName">通道名。</param>
    /// <param name="port">端口号。</param>
    /// <param name="transport">传输类型。</param>
    /// <returns>原端口号。</returns>
    public static int RemotePort(int port, string channelName, string transport)
    {
        if (port is < 1 or > 65535)
        {
            throw new ZeusChannelException(
                channelName,
                $"通道 {channelName} 的 {transport} 远端端口必须在 1 到 65535 之间。当前值：{port}。");
        }

        return port;
    }

    /// <summary>
    /// 校验本地监听或绑定端口。0 表示由操作系统分配临时端口。
    /// </summary>
    /// <param name="channelName">通道名。</param>
    /// <param name="port">本地端口号。</param>
    /// <param name="transport">传输类型。</param>
    /// <returns>原端口号。</returns>
    public static int LocalPort(int port, string channelName, string transport)
    {
        if (port is < 0 or > 65535)
        {
            throw new ZeusChannelException(
                channelName,
                $"通道 {channelName} 的 {transport} 本地端口必须在 0 到 65535 之间；0 表示自动分配。当前值：{port}。");
        }

        return port;
    }

    /// <summary>
    /// 校验正数毫秒超时。
    /// </summary>
    /// <param name="channelName">通道名。</param>
    /// <param name="milliseconds">超时毫秒数。</param>
    /// <param name="name">选项名。</param>
    /// <returns>原超时值。</returns>
    public static int PositiveMilliseconds(int milliseconds, string channelName, string name)
    {
        if (milliseconds <= 0)
        {
            throw new ZeusChannelException(
                channelName,
                $"通道 {channelName} 的 {name} 必须大于 0 毫秒。当前值：{milliseconds}。");
        }

        return milliseconds;
    }

    /// <summary>
    /// 校验非负缓冲区大小。0 表示保留系统默认值。
    /// </summary>
    /// <param name="channelName">通道名。</param>
    /// <param name="bytes">缓冲区大小。</param>
    /// <param name="name">选项名。</param>
    /// <returns>原缓冲区大小。</returns>
    public static int NonNegativeBytes(int bytes, string channelName, string name)
    {
        if (bytes < 0)
        {
            throw new ZeusChannelException(
                channelName,
                $"通道 {channelName} 的 {name} 不能为负数；0 表示保留系统默认值。当前值：{bytes}。");
        }

        return bytes;
    }
}
