using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>程序集加载时登记 EtherNet/IP 的 JSON 绑定。</summary>
internal static class EtherNetIpJsonBinderRegistration
{
    [ModuleInitializer]
    internal static void Register() => ZeusJsonBinders.Register(new EtherNetIpJsonBinder());
}

/// <summary>Allen-Bradley EtherNet/IP 的 JSON 设备与虚拟从站绑定。</summary>
public sealed class EtherNetIpJsonBinder : IZeusJsonBinder
{
    /// <inheritdoc />
    public IReadOnlyList<string> DeviceTypes { get; } = ["ethernet-ip"];

    /// <inheritdoc />
    public IReadOnlyList<string> ResponderTypes { get; } = ["ethernet-ip"];

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
        => builder.AddEtherNetIp(device.Name.Trim(), device.Channel.Trim(), null, Timeout(device), Points(device));

    /// <inheritdoc />
    public void ApplyDevice(IZeusHost host, DeviceConfiguration device)
        => host.AddEtherNetIp(device.Name.Trim(), device.Channel.Trim(), null, Timeout(device), Points(device));

    /// <inheritdoc />
    public IVirtualResponder? CreateResponder(ChannelConfiguration channel)
        => ZeusConfigurationText.Normalize(channel.Responder) == "ethernet-ip" ? new EtherNetIpSlaveResponder() : null;

    /// <inheritdoc />
    public string DeviceFingerprint(DeviceConfiguration device) => device.TimeoutMilliseconds?.ToString() ?? "";

    private static TimeSpan? Timeout(DeviceConfiguration device)
        => device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null;

    private static Action<EtherNetIpPointMap>? Points(DeviceConfiguration device)
        => device.Points.Count == 0 ? null : map =>
        {
            foreach (var point in device.Points)
            {
                var dataType = ZeusConfigurationText.Normalize(point.DataType) switch
                {
                    "bool" => EtherNetIpDataType.Bool,
                    "sint" => EtherNetIpDataType.SInt,
                    "int" => EtherNetIpDataType.Int,
                    "dint" => EtherNetIpDataType.DInt,
                    "lint" => EtherNetIpDataType.LInt,
                    "usint" => EtherNetIpDataType.USInt,
                    "uint" => EtherNetIpDataType.UInt,
                    "udint" => EtherNetIpDataType.UDInt,
                    "ulint" => EtherNetIpDataType.ULInt,
                    "real" => EtherNetIpDataType.Real,
                    "lreal" => EtherNetIpDataType.LReal,
                    _ => throw new ZeusException($"EtherNet/IP dataType「{point.DataType}」不受支持。")
                };
                var tag = string.IsNullOrWhiteSpace(point.Tag) ? point.Name : point.Tag.Trim();
                map.Tag(point.Name, tag, dataType, point.Scale, ZeusConfigurationText.CreateAlarmLimits(point));
                if (point.Writable)
                {
                    map.Writable(point.Name);
                }
            }
        };
}
