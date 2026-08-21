using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>程序集加载时登记 SNMP 的 JSON 绑定。</summary>
internal static class SnmpJsonBinderRegistration
{
    [ModuleInitializer]
    internal static void Register() => ZeusJsonBinders.Register(new SnmpJsonBinder());
}

/// <summary>SNMP v2c 的 JSON 设备与虚拟 Agent 绑定。</summary>
public sealed class SnmpJsonBinder : IZeusJsonBinder
{
    /// <inheritdoc />
    public IReadOnlyList<string> DeviceTypes { get; } = ["snmp"];

    /// <inheritdoc />
    public IReadOnlyList<string> ResponderTypes { get; } = ["snmp"];

    /// <inheritdoc />
    public void ValidateDevice(DeviceConfiguration device, string path)
    {
        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        foreach (var point in device.Points)
        {
            if (string.IsNullOrWhiteSpace(point.Oid))
            {
                throw new ZeusException($"点 {point.Name}.oid 不能为空。");
            }

            // 与原先装载器一致：非法 OID 在装载期抛协议异常，而不是拖到采集。
            _ = SnmpValue.ObjectIdentifier(point.Oid);
        }
    }

    /// <inheritdoc />
    public void ValidateResponder(ChannelConfiguration channel, string path)
    {
        if (string.IsNullOrWhiteSpace(channel.SnmpCommunity))
        {
            throw new ZeusException($"{path}.snmpCommunity 不能为空。");
        }
    }

    /// <inheritdoc />
    public void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
        => builder.AddSnmp(device.Name.Trim(), device.Channel.Trim(), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public void ApplyDevice(IZeusHost host, DeviceConfiguration device)
        => host.AddSnmp(device.Name.Trim(), device.Channel.Trim(), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public IVirtualResponder? CreateResponder(ChannelConfiguration channel)
        => ZeusConfigurationText.Normalize(channel.Responder) == "snmp"
            ? new SnmpAgentResponder(community: channel.SnmpCommunity, writeCommunity: channel.SnmpWriteCommunity)
            : null;

    /// <inheritdoc />
    public string DeviceFingerprint(DeviceConfiguration device)
        => string.Join('|', device.SnmpCommunity, device.TimeoutMilliseconds, device.SnmpInitialRequestId);

    private static TimeSpan? Timeout(DeviceConfiguration device)
        => device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null;

    private static SnmpOptions Options(DeviceConfiguration device)
        => new()
        {
            Community = device.SnmpCommunity.Trim(),
            WriteCommunity = string.IsNullOrWhiteSpace(device.SnmpWriteCommunity) ? null : device.SnmpWriteCommunity.Trim(),
            InitialRequestId = device.SnmpInitialRequestId
        };

    private static Action<SnmpPointMap>? Points(DeviceConfiguration device)
        => device.Points.Count == 0 ? null : map =>
        {
            foreach (var point in device.Points)
            {
                var oid = point.Oid!;
                var alarmLimits = ZeusConfigurationText.CreateAlarmLimits(point);
                switch (ZeusConfigurationText.Normalize(point.DataType))
                {
                    case "integer":
                        map.Integer(point.Name, oid, point.Scale, alarmLimits);
                        break;
                    case "gauge32":
                        map.Gauge32(point.Name, oid, point.Scale, alarmLimits);
                        break;
                    case "counter32":
                        map.Counter32(point.Name, oid, point.Scale, alarmLimits);
                        break;
                    case "timeticks":
                        map.TimeTicks(point.Name, oid, point.Scale, alarmLimits);
                        break;
                    case "octet-string":
                        map.OctetString(point.Name, oid);
                        break;
                    case "oid":
                        map.ObjectIdentifier(point.Name, oid);
                        break;
                    case "ip-address":
                        map.IpAddress(point.Name, oid);
                        break;
                    default:
                        map.Text(point.Name, oid);
                        break;
                }

                if (point.Writable)
                {
                    map.Writable(point.Name);
                }
            }
        };
}
