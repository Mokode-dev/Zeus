using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 注册 DL/T 645-2007 表计设备与虚拟表计。通道必须先注册。
/// </summary>
public static class ZeusHostBuilderDlt645Extensions
{
    /// <summary>在已有通道上登记一台 DL/T 645-2007 表计。</summary>
    public static ZeusHostBuilder AddDlt645(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        Dlt645Options? options = null,
        TimeSpan? timeout = null,
        Action<Dlt645PointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (services, name, channel) =>
            new Dlt645Device(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<Dlt645Device>>()));
    }

    /// <summary>在已构建的宿主上登记一台 DL/T 645-2007 表计。</summary>
    public static Dlt645Device AddDlt645(
        this IZeusHost host,
        string deviceName,
        string channelName,
        Dlt645Options? options = null,
        TimeSpan? timeout = null,
        Action<Dlt645PointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (services, name, channel) =>
            new Dlt645Device(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<Dlt645Device>>()));

    private static Dlt645PointMap? BuildMap(Action<Dlt645PointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new Dlt645PointMap();
        configure(map);
        return map;
    }
}
