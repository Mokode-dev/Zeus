using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>注册 SNMP v2c 设备。</summary>
public static class ZeusHostBuilderSnmpExtensions
{
    /// <summary>在已有通道上登记一台 SNMP v2c 设备。</summary>
    public static ZeusHostBuilder AddSnmp(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        SnmpOptions? options = null,
        TimeSpan? timeout = null,
        Action<SnmpPointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (services, name, channel) =>
            new SnmpDevice(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<SnmpDevice>>()));
    }

    /// <summary>在已构建宿主上登记一台 SNMP v2c 设备。</summary>
    public static SnmpDevice AddSnmp(
        this IZeusHost host,
        string deviceName,
        string channelName,
        SnmpOptions? options = null,
        TimeSpan? timeout = null,
        Action<SnmpPointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (services, name, channel) =>
            new SnmpDevice(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<SnmpDevice>>()));

    private static SnmpPointMap? BuildMap(Action<SnmpPointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new SnmpPointMap();
        configure(map);
        return map;
    }
}
