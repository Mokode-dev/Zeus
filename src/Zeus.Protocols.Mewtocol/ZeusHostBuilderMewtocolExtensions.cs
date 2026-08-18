namespace Zeus;

/// <summary>
/// 注册 Panasonic MEWTOCOL-COM 设备与虚拟 PLC。通道必须先注册。
/// </summary>
public static class ZeusHostBuilderMewtocolExtensions
{
    /// <summary>在已有通道上登记一台 Panasonic MEWTOCOL-COM 设备。</summary>
    public static ZeusHostBuilder AddPanasonicMewtocol(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        MewtocolOptions? options = null,
        TimeSpan? timeout = null,
        Action<MewtocolPointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (name, channel) =>
            new MewtocolDevice(name, channel, options, timeout, BuildMap(points)));
    }

    /// <summary>在已构建的宿主上登记一台 Panasonic MEWTOCOL-COM 设备。</summary>
    public static MewtocolDevice AddPanasonicMewtocol(
        this IZeusHost host,
        string deviceName,
        string channelName,
        MewtocolOptions? options = null,
        TimeSpan? timeout = null,
        Action<MewtocolPointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (name, channel) =>
            new MewtocolDevice(name, channel, options, timeout, BuildMap(points)));

    private static MewtocolPointMap? BuildMap(Action<MewtocolPointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new MewtocolPointMap();
        configure(map);
        return map;
    }
}
