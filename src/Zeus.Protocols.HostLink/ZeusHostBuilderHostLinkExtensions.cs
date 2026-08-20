using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 注册 Omron Host Link 设备与虚拟 PLC。通道必须先注册。
/// </summary>
public static class ZeusHostBuilderHostLinkExtensions
{
    /// <summary>在已有通道上登记一台 Omron Host Link 设备。</summary>
    public static ZeusHostBuilder AddOmronHostLink(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        HostLinkOptions? options = null,
        TimeSpan? timeout = null,
        Action<HostLinkPointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (services, name, channel) =>
            new HostLinkDevice(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<HostLinkDevice>>()));
    }

    /// <summary>在已构建的宿主上登记一台 Omron Host Link 设备。</summary>
    public static HostLinkDevice AddOmronHostLink(
        this IZeusHost host,
        string deviceName,
        string channelName,
        HostLinkOptions? options = null,
        TimeSpan? timeout = null,
        Action<HostLinkPointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (services, name, channel) =>
            new HostLinkDevice(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<HostLinkDevice>>()));

    private static HostLinkPointMap? BuildMap(Action<HostLinkPointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new HostLinkPointMap();
        configure(map);
        return map;
    }
}
