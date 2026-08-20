using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>注册 MQTT 主题设备。</summary>
public static class ZeusHostBuilderMqttExtensions
{
    /// <summary>在已有通道上登记一台 MQTT 设备。</summary>
    public static ZeusHostBuilder AddMqtt(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        MqttOptions? options = null,
        TimeSpan? timeout = null,
        Action<MqttPointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (services, name, channel) =>
            new MqttDevice(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<MqttDevice>>()));
    }

    /// <summary>在已构建宿主上登记一台 MQTT 设备。</summary>
    public static MqttDevice AddMqtt(
        this IZeusHost host,
        string deviceName,
        string channelName,
        MqttOptions? options = null,
        TimeSpan? timeout = null,
        Action<MqttPointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (services, name, channel) =>
            new MqttDevice(name, channel, options, timeout, BuildMap(points), services.GetService<ILogger<MqttDevice>>()));

    private static MqttPointMap? BuildMap(Action<MqttPointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new MqttPointMap();
        configure(map);
        return map;
    }
}
