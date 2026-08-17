namespace Zeus;

/// <summary>
/// 注册 Omron FINS 设备与虚拟 PLC。通道必须先注册。
/// </summary>
public static class ZeusHostBuilderFinsExtensions
{
    /// <summary>在已有通道上登记一台 Omron FINS/UDP 设备。</summary>
    public static ZeusHostBuilder AddOmronFinsUdp(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        FinsOptions? options = null,
        TimeSpan? timeout = null,
        Action<FinsPointMap>? points = null)
        => AddOmronFins(builder, deviceName, channelName, FinsTransport.Udp, options, timeout, points);

    /// <summary>在已有通道上登记一台 Omron FINS/TCP 设备。</summary>
    public static ZeusHostBuilder AddOmronFinsTcp(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        FinsOptions? options = null,
        TimeSpan? timeout = null,
        Action<FinsPointMap>? points = null)
        => AddOmronFins(builder, deviceName, channelName, FinsTransport.Tcp, options, timeout, points);

    /// <summary>在已有通道上登记一台 Omron FINS 设备。</summary>
    public static ZeusHostBuilder AddOmronFins(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        FinsTransport transport = FinsTransport.Udp,
        FinsOptions? options = null,
        TimeSpan? timeout = null,
        Action<FinsPointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (name, channel) =>
            new FinsDevice(name, channel, transport, options, timeout, BuildMap(points)));
    }

    /// <summary>在已构建的宿主上登记一台 Omron FINS/UDP 设备。</summary>
    public static FinsDevice AddOmronFinsUdp(
        this IZeusHost host,
        string deviceName,
        string channelName,
        FinsOptions? options = null,
        TimeSpan? timeout = null,
        Action<FinsPointMap>? points = null)
        => AddOmronFins(host, deviceName, channelName, FinsTransport.Udp, options, timeout, points);

    /// <summary>在已构建的宿主上登记一台 Omron FINS/TCP 设备。</summary>
    public static FinsDevice AddOmronFinsTcp(
        this IZeusHost host,
        string deviceName,
        string channelName,
        FinsOptions? options = null,
        TimeSpan? timeout = null,
        Action<FinsPointMap>? points = null)
        => AddOmronFins(host, deviceName, channelName, FinsTransport.Tcp, options, timeout, points);

    /// <summary>在已构建的宿主上登记一台 Omron FINS 设备。</summary>
    public static FinsDevice AddOmronFins(
        this IZeusHost host,
        string deviceName,
        string channelName,
        FinsTransport transport = FinsTransport.Udp,
        FinsOptions? options = null,
        TimeSpan? timeout = null,
        Action<FinsPointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (name, channel) =>
            new FinsDevice(name, channel, transport, options, timeout, BuildMap(points)));

    private static FinsPointMap? BuildMap(Action<FinsPointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new FinsPointMap();
        configure(map);
        return map;
    }
}
