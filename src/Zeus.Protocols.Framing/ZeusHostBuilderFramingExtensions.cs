namespace Zeus;

/// <summary>
/// 在已有通道上创建自定义帧会话。会话不登记进目录，由调用方持有或交给设备。
/// </summary>
public static class ZeusHostBuilderFramingExtensions
{
    /// <summary>
    /// 按默认布局（<c>AA 55</c> + 单字节长度 + 无校验）创建会话。
    /// </summary>
    /// <param name="host">已构建的宿主。</param>
    /// <param name="channelName">通道名。</param>
    /// <param name="timeout">应答超时。</param>
    public static FrameSession CreateFrameSession(this IZeusHost host, string channelName, TimeSpan? timeout = null)
        => host.CreateFrameSession(channelName, new FrameLayout(), timeout);

    /// <summary>
    /// 按指定布局创建会话。
    /// </summary>
    /// <param name="host">已构建的宿主。</param>
    /// <param name="channelName">通道名。</param>
    /// <param name="layout">帧布局。</param>
    /// <param name="timeout">应答超时。</param>
    public static FrameSession CreateFrameSession(
        this IZeusHost host,
        string channelName,
        FrameLayout layout,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(layout);
        var channel = host.Channels.Get(channelName);
        return new FrameSession(channel, new LengthHeaderFrameCodec(layout), timeout);
    }
}
