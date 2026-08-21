using System.Runtime.CompilerServices;
using System.Text;

namespace Zeus;

/// <summary>程序集加载时登记 MQTT 的 JSON 绑定。</summary>
internal static class MqttJsonBinderRegistration
{
    [ModuleInitializer]
    internal static void Register() => ZeusJsonBinders.Register(new MqttJsonBinder());
}

/// <summary>MQTT 的 JSON 设备与虚拟 Broker 绑定。</summary>
public sealed class MqttJsonBinder : IZeusJsonBinder
{
    /// <inheritdoc />
    public IReadOnlyList<string> DeviceTypes { get; } = ["mqtt"];

    /// <inheritdoc />
    public IReadOnlyList<string> ResponderTypes { get; } = ["mqtt"];

    /// <inheritdoc />
    public void ValidateDevice(DeviceConfiguration device, string path)
    {
        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        foreach (var point in device.Points)
        {
            var topic = string.IsNullOrWhiteSpace(point.Topic) ? point.Name : point.Topic.Trim();
            if (topic.Contains('+') || topic.Contains('#'))
            {
                throw new ZeusException($"{path} 点 {point.Name} 的 topic 不能包含 MQTT 通配符。");
            }
        }
    }

    /// <inheritdoc />
    public void ValidateResponder(ChannelConfiguration channel, string path)
    {
    }

    /// <inheritdoc />
    public void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
        => builder.AddMqtt(device.Name.Trim(), device.Channel.Trim(), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public void ApplyDevice(IZeusHost host, DeviceConfiguration device)
        => host.AddMqtt(device.Name.Trim(), device.Channel.Trim(), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public IVirtualResponder? CreateResponder(ChannelConfiguration channel)
        => ZeusConfigurationText.Normalize(channel.Responder) == "mqtt" ? new MqttBrokerResponder() : null;

    /// <inheritdoc />
    public string DeviceFingerprint(DeviceConfiguration device)
        => string.Join('|', device.MqttClientId, device.TimeoutMilliseconds);

    private static TimeSpan? Timeout(DeviceConfiguration device)
        => device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null;

    private static MqttOptions Options(DeviceConfiguration device)
        => new()
        {
            ClientId = device.MqttClientId,
            Username = device.MqttUsername,
            Password = device.MqttPassword,
            KeepAliveSeconds = checked((ushort)device.MqttKeepAliveSeconds),
            CleanSession = device.MqttCleanSession,
            WillTopic = device.MqttWillTopic,
            WillPayload = device.MqttWillPayload is null ? null : Encoding.UTF8.GetBytes(device.MqttWillPayload),
            WillQualityOfService = ParseQos(device.MqttWillQos),
            WillRetain = device.MqttWillRetain,
            MaximumPacketSize = device.MqttMaximumPacketSize,
            AutomaticKeepAlive = device.MqttAutomaticKeepAlive,
            AutomaticReconnect = device.MqttAutomaticReconnect
        };

    private static Action<MqttPointMap>? Points(DeviceConfiguration device)
        => device.Points.Count == 0 ? null : map =>
        {
            foreach (var point in device.Points)
            {
                var topic = string.IsNullOrWhiteSpace(point.Topic) ? point.Name : point.Topic.Trim();
                var alarmLimits = ZeusConfigurationText.CreateAlarmLimits(point);
                switch (ZeusConfigurationText.Normalize(point.DataType))
                {
                    case "boolean":
                        map.Boolean(point.Name, topic);
                        break;
                    case "int32":
                        map.Int32(point.Name, topic, alarmLimits);
                        break;
                    case "int64":
                        map.Int64(point.Name, topic, alarmLimits);
                        break;
                    case "double":
                        map.Double(point.Name, topic, alarmLimits);
                        break;
                    case "bytes":
                        map.Bytes(point.Name, topic);
                        break;
                    default:
                        map.Text(point.Name, topic);
                        break;
                }

                if (point.Writable)
                {
                    map.Writable(point.Name);
                }

                map.WithQualityOfService(point.Name, ParseQos(point.MqttQos));
                map.Retained(point.Name, point.MqttRetain);
            }
        };

    private static MqttQualityOfService ParseQos(string? value)
        => ZeusConfigurationText.Normalize(value) switch
        {
            "1" => MqttQualityOfService.AtLeastOnce,
            "2" => MqttQualityOfService.ExactlyOnce,
            _ => MqttQualityOfService.AtMostOnce
        };
}
