using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>程序集加载时登记 Omron FINS 的 JSON 绑定。</summary>
internal static class FinsJsonBinderRegistration
{
    [ModuleInitializer]
    internal static void Register() => ZeusJsonBinders.Register(new FinsJsonBinder());
}

/// <summary>Omron FINS 的 JSON 设备与虚拟从站绑定。</summary>
public sealed class FinsJsonBinder : IZeusJsonBinder
{
    /// <inheritdoc />
    public IReadOnlyList<string> DeviceTypes { get; } = ["omron-fins-udp", "omron-fins-tcp"];

    /// <inheritdoc />
    public IReadOnlyList<string> ResponderTypes { get; } = ["fins"];

    /// <inheritdoc />
    public void ValidateDevice(DeviceConfiguration device, string path)
    {
        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ParseWordOrder(device.WordOrder, $"{path}.wordOrder");
        ValidatePoints(device.Points, path);
    }

    /// <inheritdoc />
    public void ValidateResponder(ChannelConfiguration channel, string path)
    {
        var transport = ZeusConfigurationText.Normalize(channel.Transport);
        if (transport is not ("udp" or "tcp"))
        {
            throw new ZeusException($"{path}.transport「{channel.Transport}」不受支持。FINS 虚拟从站可选 udp、tcp。");
        }
    }

    /// <inheritdoc />
    public void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
        => builder.AddOmronFins(device.Name.Trim(), device.Channel.Trim(), Transport(device), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public void ApplyDevice(IZeusHost host, DeviceConfiguration device)
        => host.AddOmronFins(device.Name.Trim(), device.Channel.Trim(), Transport(device), Options(device), Timeout(device), Points(device));

    /// <inheritdoc />
    public IVirtualResponder? CreateResponder(ChannelConfiguration channel)
        => ZeusConfigurationText.Normalize(channel.Responder) == "fins"
            ? new FinsSlaveResponder(ZeusConfigurationText.Normalize(channel.Transport) == "tcp" ? FinsTransport.Tcp : FinsTransport.Udp)
            : null;

    /// <inheritdoc />
    public string DeviceFingerprint(DeviceConfiguration device)
        => string.Join('|', ZeusConfigurationText.Normalize(device.Type), device.TimeoutMilliseconds, device.DestinationNode, device.SourceNode, ZeusConfigurationText.Normalize(device.WordOrder));

    private static FinsTransport Transport(DeviceConfiguration device)
        => ZeusConfigurationText.Normalize(device.Type) == "omron-fins-tcp" ? FinsTransport.Tcp : FinsTransport.Udp;

    private static TimeSpan? Timeout(DeviceConfiguration device)
        => device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null;

    private static Action<FinsPointMap>? Points(DeviceConfiguration device)
        => device.Points.Count == 0 ? null : map => ApplyPoints(map, device.Points);

    private static FinsOptions Options(DeviceConfiguration device)
        => new()
        {
            DestinationNetwork = (byte)device.DestinationNetwork,
            DestinationNode = (byte)device.DestinationNode,
            DestinationUnit = (byte)device.DestinationUnit,
            SourceNetwork = (byte)device.SourceNetwork,
            SourceNode = (byte)device.SourceNode,
            SourceUnit = (byte)device.SourceUnit,
            GatewayCount = (byte)device.GatewayCount,
            InformationControlField = (byte)device.InformationControlField,
            TcpRequestedClientNode = (byte)device.TcpRequestedClientNode,
            UseTcpNodeAddressHandshake = device.UseTcpNodeAddressHandshake,
            WordOrder = ParseWordOrder(device.WordOrder, "device.wordOrder")
        };

    private static void ValidatePoints(List<PointConfiguration> points, string devicePath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            ZeusConfigurationText.EnsureName(point.Name, $"{devicePath}.points");
            if (!names.Add(point.Name.Trim()))
            {
                throw new ZeusException($"{devicePath} 点名 {point.Name} 重复。");
            }

            if (string.IsNullOrWhiteSpace(point.Area))
            {
                throw new ZeusException($"点 {point.Name}.area 必须指定。");
            }

            var dataType = ParseDataType(point.DataType, $"point {point.Name}.dataType");
            ParseArea(point.Area, dataType, $"point {point.Name}.area");
            ZeusConfigurationText.ValidatePointAlarms(point, $"point {point.Name}");
        }
    }

    private static void ApplyPoints(FinsPointMap map, List<PointConfiguration> points)
    {
        foreach (var point in points)
        {
            var dataType = ParseDataType(point.DataType, $"point {point.Name}.dataType");
            var area = ParseArea(point.Area, dataType, $"point {point.Name}.area");
            var alarmLimits = ZeusConfigurationText.CreateAlarmLimits(point);
            if (dataType == FinsDataType.Bit)
            {
                map.Bit(point.Name, area, (ushort)point.Address, (byte)point.BitOffset);
            }
            else if (point.Scale is { } scale)
            {
                map.Word(point.Name, area, (ushort)point.Address, scale);
                if (alarmLimits is not null)
                {
                    map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
                }
            }
            else
            {
                map.Word(point.Name, area, (ushort)point.Address);
                if (alarmLimits is not null)
                {
                    map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
                }
            }

            if (point.Writable)
            {
                map.Writable(point.Name);
            }
        }
    }

    private static FinsDataType ParseDataType(string? value, string path)
        => ZeusConfigurationText.Normalize(value) switch
        {
            "" or "word" => FinsDataType.Word,
            "bit" => FinsDataType.Bit,
            "int16" => FinsDataType.Int16,
            "uint32" => FinsDataType.UInt32,
            "int32" => FinsDataType.Int32,
            "real" => FinsDataType.Real,
            _ => throw new ZeusException($"{path}「{value}」不受支持。")
        };

    private static FinsMemoryAreaCode ParseArea(string? value, FinsDataType dataType, string path)
    {
        var token = ZeusConfigurationText.Normalize(value);
        var bit = dataType == FinsDataType.Bit;
        return token switch
        {
            "cio" => bit ? FinsMemoryAreaCode.CioBit : FinsMemoryAreaCode.CioWord,
            "wr" => bit ? FinsMemoryAreaCode.WorkBit : FinsMemoryAreaCode.WorkWord,
            "hr" => bit ? FinsMemoryAreaCode.HoldingBit : FinsMemoryAreaCode.HoldingWord,
            "ar" => bit ? FinsMemoryAreaCode.AuxiliaryBit : FinsMemoryAreaCode.AuxiliaryWord,
            "dm" => bit ? FinsMemoryAreaCode.DataMemoryBit : FinsMemoryAreaCode.DataMemoryWord,
            "tc" => bit ? FinsMemoryAreaCode.TimerCounterFlag : FinsMemoryAreaCode.TimerCounterValue,
            "em" => bit ? FinsMemoryAreaCode.CurrentEmBit : FinsMemoryAreaCode.CurrentEmWord,
            _ => throw new ZeusException($"{path}「{value}」不受支持。")
        };
    }

    private static FinsWordOrder ParseWordOrder(string? value, string path)
        => ZeusConfigurationText.Normalize(value) switch
        {
            "" or "high-word-first" => FinsWordOrder.HighWordFirst,
            "low-word-first" => FinsWordOrder.LowWordFirst,
            _ => throw new ZeusException($"{path}「{value}」不受支持。")
        };
}
