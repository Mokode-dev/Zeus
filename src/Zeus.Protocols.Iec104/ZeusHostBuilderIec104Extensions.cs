using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 注册 IEC 60870-5-104 站设备。通道必须先注册。
/// </summary>
public static class ZeusHostBuilderIec104Extensions
{
    /// <summary>在已有通道上登记一台 IEC104 站。</summary>
    public static ZeusHostBuilder AddIec104(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        Iec104Options? options = null,
        TimeSpan? timeout = null,
        Action<Iec104PointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (services, name, channel) =>
            new Iec104Device(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<Iec104Device>>()));
    }

    /// <summary>在已构建的宿主上登记一台 IEC104 站。</summary>
    public static Iec104Device AddIec104(
        this IZeusHost host,
        string deviceName,
        string channelName,
        Iec104Options? options = null,
        TimeSpan? timeout = null,
        Action<Iec104PointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (services, name, channel) =>
            new Iec104Device(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<Iec104Device>>()));

    private static Iec104PointMap? BuildMap(Action<Iec104PointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new Iec104PointMap();
        configure(map);
        return map;
    }
}
