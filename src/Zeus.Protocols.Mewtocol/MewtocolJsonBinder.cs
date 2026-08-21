using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>程序集加载时登记 Panasonic MEWTOCOL 的 JSON 绑定。</summary>
internal static class MewtocolJsonBinderRegistration
{
    [ModuleInitializer]
    internal static void Register() => ZeusJsonBinders.Register(new MewtocolJsonBinder());
}

/// <summary>Panasonic MEWTOCOL 的 JSON 设备与虚拟从站绑定。</summary>
public sealed class MewtocolJsonBinder : IZeusJsonBinder
{
    /// <inheritdoc />
    public IReadOnlyList<string> DeviceTypes { get; } = ["panasonic-mewtocol"];

    /// <inheritdoc />
    public IReadOnlyList<string> ResponderTypes { get; } = ["mewtocol"];

    /// <inheritdoc />
    public void ValidateDevice(DeviceConfiguration device, string path)
    {
        if (device.UnitId is < 1 or > 99)
        {
            throw new ZeusException($"{path}.unitId 必须介于 1 与 99 之间。");
        }

        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }
    }

    /// <inheritdoc />
    public void ValidateResponder(ChannelConfiguration channel, string path)
    {
        if (channel.UnitId is < 1 or > 99)
        {
            throw new ZeusException($"{path}.unitId 必须介于 1 与 99 之间。");
        }
    }

    /// <inheritdoc />
    public void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
        => builder.AddPanasonicMewtocol(device.Name.Trim(), device.Channel.Trim(), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public void ApplyDevice(IZeusHost host, DeviceConfiguration device)
        => host.AddPanasonicMewtocol(device.Name.Trim(), device.Channel.Trim(), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public IVirtualResponder? CreateResponder(ChannelConfiguration channel)
        => ZeusConfigurationText.Normalize(channel.Responder) == "mewtocol"
            ? new MewtocolSlaveResponder(channel.UnitId)
            : null;

    /// <inheritdoc />
    public string DeviceFingerprint(DeviceConfiguration device)
        => string.Join('|', device.UnitId, device.TimeoutMilliseconds);

    private static TimeSpan? Timeout(DeviceConfiguration device)
        => device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null;

    private static MewtocolOptions Options(DeviceConfiguration device)
        => new() { StationNumber = device.UnitId };

    private static Action<MewtocolPointMap>? Points(DeviceConfiguration device)
        => device.Points.Count == 0 ? null : map =>
        {
            foreach (var point in device.Points)
            {
                var area = ZeusConfigurationText.Normalize(point.Area);
                var dataType = ZeusConfigurationText.Normalize(point.DataType);
                if (area is "x" or "y" or "r" or "l")
                {
                    var contact = area switch
                    {
                        "x" => MewtocolContactArea.ExternalInput,
                        "y" => MewtocolContactArea.ExternalOutput,
                        "l" => MewtocolContactArea.LinkRelay,
                        _ => MewtocolContactArea.InternalRelay
                    };
                    if (dataType is "bit")
                    {
                        map.Bit(point.Name, contact, point.Address, (byte)point.BitOffset);
                    }
                    else if (point.Scale is { } scale)
                    {
                        map.Word(point.Name, contact, point.Address, scale);
                    }
                    else
                    {
                        map.Word(point.Name, contact, point.Address);
                    }
                }
                else
                {
                    var data = area switch
                    {
                        "ld" => MewtocolDataArea.LinkDataRegister,
                        "fl" => MewtocolDataArea.FileRegister,
                        _ => MewtocolDataArea.DataRegister
                    };
                    if (dataType is "bit")
                    {
                        map.Bit(point.Name, data, point.Address, (byte)point.BitOffset);
                    }
                    else if (point.Scale is { } scale)
                    {
                        map.Word(point.Name, data, point.Address, scale);
                    }
                    else
                    {
                        map.Word(point.Name, data, point.Address);
                    }
                }

                if (point.Writable)
                {
                    map.Writable(point.Name);
                }
            }
        };
}
