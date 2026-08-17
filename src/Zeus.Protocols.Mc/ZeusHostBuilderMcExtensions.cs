namespace Zeus;

/// <summary>
/// 注册 Mitsubishi MC Protocol 设备。
/// </summary>
public static class ZeusHostBuilderMcExtensions
{
    /// <summary>
    /// 在已有通道上登记一台 Mitsubishi MC 设备。默认使用 3E Binary。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="deviceName">设备名。</param>
    /// <param name="channelName">TCP 或虚拟通道名。</param>
    /// <param name="options">MC 帧选项。</param>
    /// <param name="timeout">应答超时。</param>
    /// <param name="points">可选点表。声明后由宿主采集循环自动轮询。</param>
    public static ZeusHostBuilder AddMitsubishiMc(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        Mc3EOptions? options = null,
        TimeSpan? timeout = null,
        Action<McPointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (name, channel) =>
            new McDevice(name, channel, options, timeout, BuildMap(points)));
    }

    /// <summary>
    /// 在已有通道上登记一台 Mitsubishi MC 3E Binary 设备。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="deviceName">设备名。</param>
    /// <param name="channelName">TCP 或虚拟通道名。</param>
    /// <param name="options">3E 帧头选项。</param>
    /// <param name="timeout">应答超时。</param>
    /// <param name="points">可选点表。声明后由宿主采集循环自动轮询。</param>
    public static ZeusHostBuilder AddMitsubishiMc3E(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        Mc3EOptions? options = null,
        TimeSpan? timeout = null,
        Action<McPointMap>? points = null)
    {
        return builder.AddMitsubishiMc(deviceName, channelName, options, timeout, points);
    }

    /// <summary>
    /// 在已构建的宿主上登记一台 Mitsubishi MC 设备。默认使用 3E Binary。
    /// </summary>
    public static McDevice AddMitsubishiMc(
        this IZeusHost host,
        string deviceName,
        string channelName,
        Mc3EOptions? options = null,
        TimeSpan? timeout = null,
        Action<McPointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (name, channel) =>
            new McDevice(name, channel, options, timeout, BuildMap(points)));

    /// <summary>
    /// 在已构建的宿主上登记一台 Mitsubishi MC 3E Binary 设备。
    /// </summary>
    public static McDevice AddMitsubishiMc3E(
        this IZeusHost host,
        string deviceName,
        string channelName,
        Mc3EOptions? options = null,
        TimeSpan? timeout = null,
        Action<McPointMap>? points = null)
        => host.AddMitsubishiMc(deviceName, channelName, options, timeout, points);

    private static McPointMap? BuildMap(Action<McPointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new McPointMap();
        configure(map);
        return map;
    }
}
