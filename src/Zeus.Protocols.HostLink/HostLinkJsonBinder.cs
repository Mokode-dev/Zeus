using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>程序集加载时登记 Omron Host Link 的 JSON 绑定。</summary>
internal static class HostLinkJsonBinderRegistration
{
    [ModuleInitializer]
    internal static void Register() => ZeusJsonBinders.Register(new HostLinkJsonBinder());
}

/// <summary>Omron Host Link 的 JSON 设备与虚拟从站绑定。</summary>
public sealed class HostLinkJsonBinder : IZeusJsonBinder
{
    /// <inheritdoc />
    public IReadOnlyList<string> DeviceTypes { get; } = ["omron-host-link"];

    /// <inheritdoc />
    public IReadOnlyList<string> ResponderTypes { get; } = ["host-link"];

    /// <inheritdoc />
    public void ValidateDevice(DeviceConfiguration device, string path)
    {
        if (device.UnitId > 31)
        {
            throw new ZeusException($"{path}.unitId 必须介于 0 与 31 之间。");
        }

        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        foreach (var point in device.Points)
        {
            ZeusConfigurationText.EnsureName(point.Name, path);
            if (string.IsNullOrWhiteSpace(point.Area))
            {
                throw new ZeusException($"点 {point.Name}.area 必须指定。");
            }
        }
    }

    /// <inheritdoc />
    public void ValidateResponder(ChannelConfiguration channel, string path)
    {
        if (channel.UnitId > 31)
        {
            throw new ZeusException($"{path}.unitId 必须介于 0 与 31 之间。");
        }
    }

    /// <inheritdoc />
    public void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
        => builder.AddOmronHostLink(device.Name.Trim(), device.Channel.Trim(), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public void ApplyDevice(IZeusHost host, DeviceConfiguration device)
        => host.AddOmronHostLink(device.Name.Trim(), device.Channel.Trim(), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public IVirtualResponder? CreateResponder(ChannelConfiguration channel)
        => ZeusConfigurationText.Normalize(channel.Responder) == "host-link"
            ? new HostLinkSlaveResponder(channel.UnitId)
            : null;

    /// <inheritdoc />
    public string DeviceFingerprint(DeviceConfiguration device)
        => string.Join('|', device.UnitId, device.TimeoutMilliseconds, ZeusConfigurationText.Normalize(device.WordOrder));

    private static TimeSpan? Timeout(DeviceConfiguration device)
        => device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null;

    private static HostLinkOptions Options(DeviceConfiguration device)
        => new()
        {
            UnitNumber = device.UnitId,
            WordOrder = ZeusConfigurationText.Normalize(device.WordOrder) == "low-word-first"
                ? HostLinkWordOrder.LowWordFirst
                : HostLinkWordOrder.HighWordFirst
        };

    private static Action<HostLinkPointMap>? Points(DeviceConfiguration device)
        => device.Points.Count == 0 ? null : map =>
        {
            foreach (var point in device.Points)
            {
                var area = ParseArea(point.Area);
                var dataType = ZeusConfigurationText.Normalize(point.DataType);
                if (dataType is "bit")
                {
                    map.Bit(point.Name, area, (ushort)point.Address, (byte)point.BitOffset);
                }
                else if (point.Scale is { } scale)
                {
                    map.Word(point.Name, area, (ushort)point.Address, scale);
                }
                else
                {
                    map.Word(point.Name, area, (ushort)point.Address);
                }

                if (point.Writable)
                {
                    map.Writable(point.Name);
                }
            }
        };

    private static HostLinkArea ParseArea(string? value)
        => ZeusConfigurationText.Normalize(value) switch
        {
            "cio" => HostLinkArea.Cio,
            "lr" => HostLinkArea.Link,
            "hr" => HostLinkArea.Holding,
            "ar" => HostLinkArea.Auxiliary,
            "dm" => HostLinkArea.DataMemory,
            _ => throw new ZeusException($"Host Link area「{value}」不受支持。")
        };
}
