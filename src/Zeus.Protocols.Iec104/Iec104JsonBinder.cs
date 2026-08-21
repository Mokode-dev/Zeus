using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>程序集加载时登记 IEC104 的 JSON 绑定。</summary>
internal static class Iec104JsonBinderRegistration
{
    [ModuleInitializer]
    internal static void Register() => ZeusJsonBinders.Register(new Iec104JsonBinder());
}

/// <summary>IEC 60870-5-104 的 JSON 设备与虚拟站绑定。</summary>
public sealed class Iec104JsonBinder : IZeusJsonBinder
{
    /// <inheritdoc />
    public IReadOnlyList<string> DeviceTypes { get; } = ["iec104"];

    /// <inheritdoc />
    public IReadOnlyList<string> ResponderTypes { get; } = ["iec104"];

    /// <inheritdoc />
    public void ValidateDevice(DeviceConfiguration device, string path)
    {
        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }
    }

    /// <inheritdoc />
    public void ValidateResponder(ChannelConfiguration channel, string path)
    {
    }

    /// <inheritdoc />
    public void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
        => builder.AddIec104(device.Name.Trim(), device.Channel.Trim(), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public void ApplyDevice(IZeusHost host, DeviceConfiguration device)
        => host.AddIec104(device.Name.Trim(), device.Channel.Trim(), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public IVirtualResponder? CreateResponder(ChannelConfiguration channel)
        => ZeusConfigurationText.Normalize(channel.Responder) == "iec104"
            ? new Iec104SlaveResponder(new Iec104Options { CommonAddress = channel.CommonAddress })
            : null;

    /// <inheritdoc />
    public string DeviceFingerprint(DeviceConfiguration device)
        => string.Join('|', device.CommonAddress, device.TimeoutMilliseconds, device.T1Milliseconds, device.T2Milliseconds, device.T3Milliseconds);

    private static TimeSpan? Timeout(DeviceConfiguration device)
        => device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null;

    private static Iec104Options Options(DeviceConfiguration device)
        => new()
        {
            CommonAddress = device.CommonAddress,
            OriginatorAddress = device.OriginatorAddress,
            InterrogationQualifier = device.InterrogationQualifier,
            T1 = TimeSpan.FromMilliseconds(device.T1Milliseconds),
            T2 = TimeSpan.FromMilliseconds(device.T2Milliseconds),
            T3 = TimeSpan.FromMilliseconds(device.T3Milliseconds),
            MaxUnacknowledgedIFrames = device.MaxUnacknowledgedIFrames,
            AcknowledgeWindow = device.AcknowledgeWindow
        };

    private static Action<Iec104PointMap>? Points(DeviceConfiguration device)
        => device.Points.Count == 0 ? null : map =>
        {
            foreach (var point in device.Points)
            {
                var dataType = ZeusConfigurationText.Normalize(point.DataType);
                var alarmLimits = ZeusConfigurationText.CreateAlarmLimits(point);
                switch (dataType)
                {
                    case "single-point":
                        map.SinglePoint(point.Name, point.Address);
                        break;
                    case "normalized":
                        map.Normalized(point.Name, point.Address, point.Scale, alarmLimits);
                        break;
                    case "short-float":
                        map.ShortFloat(point.Name, point.Address, point.Scale, alarmLimits);
                        break;
                    default:
                        map.Scaled(point.Name, point.Address, point.Scale, alarmLimits);
                        break;
                }

                if (point.Writable)
                {
                    map.Writable(point.Name);
                }
            }
        };
}
