namespace Zeus;

/// <summary>
/// 注册 Siemens S7 设备。
/// </summary>
public static class ZeusHostBuilderS7Extensions
{
    /// <summary>
    /// 在已有通道上登记一台 Siemens S7 设备。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="deviceName">设备名。</param>
    /// <param name="channelName">TCP 或虚拟通道名。</param>
    /// <param name="options">S7 会话选项。</param>
    /// <param name="timeout">应答超时。</param>
    /// <param name="points">可选点表。声明后由宿主采集循环自动轮询。</param>
    public static ZeusHostBuilder AddSiemensS7(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        S7Options? options = null,
        TimeSpan? timeout = null,
        Action<S7PointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (name, channel) =>
            new S7Device(name, channel, options, timeout, BuildMap(points)));
    }

    /// <summary>
    /// 在已构建的宿主上登记一台 Siemens S7 设备。
    /// </summary>
    public static S7Device AddSiemensS7(
        this IZeusHost host,
        string deviceName,
        string channelName,
        S7Options? options = null,
        TimeSpan? timeout = null,
        Action<S7PointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (name, channel) =>
            new S7Device(name, channel, options, timeout, BuildMap(points)));

    private static S7PointMap? BuildMap(Action<S7PointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new S7PointMap();
        configure(map);
        return map;
    }
}
