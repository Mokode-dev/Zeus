using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 注册 Allen-Bradley EtherNet/IP 设备与虚拟 PLC。通道必须先注册。
/// </summary>
public static class ZeusHostBuilderEtherNetIpExtensions
{
    /// <summary>在已有通道上登记一台 Allen-Bradley EtherNet/IP 设备。</summary>
    public static ZeusHostBuilder AddAllenBradleyEtherNetIp(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        EtherNetIpOptions? options = null,
        TimeSpan? timeout = null,
        Action<EtherNetIpPointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (services, name, channel) =>
            new EtherNetIpDevice(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<EtherNetIpDevice>>()));
    }

    /// <summary>在已有通道上登记一台 Allen-Bradley EtherNet/IP 设备。</summary>
    public static ZeusHostBuilder AddEtherNetIp(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        EtherNetIpOptions? options = null,
        TimeSpan? timeout = null,
        Action<EtherNetIpPointMap>? points = null)
        => AddAllenBradleyEtherNetIp(builder, deviceName, channelName, options, timeout, points);

    /// <summary>在已构建的宿主上登记一台 Allen-Bradley EtherNet/IP 设备。</summary>
    public static EtherNetIpDevice AddAllenBradleyEtherNetIp(
        this IZeusHost host,
        string deviceName,
        string channelName,
        EtherNetIpOptions? options = null,
        TimeSpan? timeout = null,
        Action<EtherNetIpPointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (services, name, channel) =>
            new EtherNetIpDevice(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<EtherNetIpDevice>>()));

    /// <summary>在已构建的宿主上登记一台 Allen-Bradley EtherNet/IP 设备。</summary>
    public static EtherNetIpDevice AddEtherNetIp(
        this IZeusHost host,
        string deviceName,
        string channelName,
        EtherNetIpOptions? options = null,
        TimeSpan? timeout = null,
        Action<EtherNetIpPointMap>? points = null)
        => AddAllenBradleyEtherNetIp(host, deviceName, channelName, options, timeout, points);

    private static EtherNetIpPointMap? BuildMap(Action<EtherNetIpPointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new EtherNetIpPointMap();
        configure(map);
        return map;
    }
}
