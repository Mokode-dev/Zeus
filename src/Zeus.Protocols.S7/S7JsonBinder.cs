using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>
/// 程序集加载时登记 Siemens S7 的 JSON 绑定。
/// </summary>
internal static class S7JsonBinderRegistration
{
    /// <summary>模块初始化：登记 <see cref="S7JsonBinder"/>。</summary>
    [ModuleInitializer]
    internal static void Register() => ZeusJsonBinders.Register(new S7JsonBinder());
}

/// <summary>
/// Siemens S7 的 JSON 设备与虚拟从站绑定。
/// </summary>
public sealed class S7JsonBinder : IZeusJsonBinder
{
    /// <inheritdoc />
    public IReadOnlyList<string> DeviceTypes { get; } = ["siemens-s7"];

    /// <inheritdoc />
    public IReadOnlyList<string> ResponderTypes { get; } = ["s7"];

    /// <inheritdoc />
    public void ValidateDevice(DeviceConfiguration device, string path)
    {
        ValidateByte(device.Rack, $"{path}.rack");
        if (device.Slot is < 0 or > 31)
        {
            throw new ZeusException($"{path}.slot 必须介于 0 与 31 之间。");
        }

        ValidateUInt16(device.LocalTsap, $"{path}.localTsap");
        if (device.RemoteTsap is { } remoteTsap)
        {
            ValidateUInt16(remoteTsap, $"{path}.remoteTsap");
        }

        if (device.RequestedPduLength is < 128 or > 960)
        {
            throw new ZeusException($"{path}.requestedPduLength 必须介于 128 与 960 之间。");
        }

        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidatePoints(device.Points, path);
    }

    /// <inheritdoc />
    public void ValidateResponder(ChannelConfiguration channel, string path)
    {
    }

    /// <inheritdoc />
    public void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
    {
        var timeout = device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : (TimeSpan?)null;
        Action<S7PointMap>? points = device.Points.Count == 0 ? null : map => ApplyPoints(map, device.Points);
        builder.AddSiemensS7(device.Name.Trim(), device.Channel.Trim(), CreateOptions(device), timeout, points);
    }

    /// <inheritdoc />
    public void ApplyDevice(IZeusHost host, DeviceConfiguration device)
    {
        var timeout = device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : (TimeSpan?)null;
        Action<S7PointMap>? points = device.Points.Count == 0 ? null : map => ApplyPoints(map, device.Points);
        host.AddSiemensS7(device.Name.Trim(), device.Channel.Trim(), CreateOptions(device), timeout, points);
    }

    /// <inheritdoc />
    public IVirtualResponder? CreateResponder(ChannelConfiguration channel)
        => ZeusConfigurationText.Normalize(channel.Responder) == "s7" ? new S7SlaveResponder() : null;

    /// <inheritdoc />
    public string DeviceFingerprint(DeviceConfiguration device)
        => string.Join('|', device.TimeoutMilliseconds, device.Rack, device.Slot, device.LocalTsap, device.RemoteTsap, device.RequestedPduLength);

    private static S7Options CreateOptions(DeviceConfiguration device)
        => new()
        {
            Rack = (byte)device.Rack,
            Slot = (byte)device.Slot,
            LocalTsap = (ushort)device.LocalTsap,
            RemoteTsap = device.RemoteTsap is { } remoteTsap ? (ushort)remoteTsap : null,
            RequestedPduLength = (ushort)device.RequestedPduLength
        };

    private static void ValidatePoints(List<PointConfiguration> points, string devicePath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var path = $"{devicePath}.points[{i}]";
            ZeusConfigurationText.EnsureName(point.Name, path);
            if (!names.Add(point.Name.Trim()))
            {
                throw new ZeusException($"{path}.name「{point.Name}」在同一设备内重复。");
            }

            if (string.IsNullOrWhiteSpace(point.Area))
            {
                throw new ZeusException($"{path}.area 必须指定。S7 可选 db、m、i、q。");
            }

            var area = ParseArea(point.Area, $"{path}.area");
            var dataType = ParseDataType(point.DataType, $"{path}.dataType");
            if (point.Address is < 0 or > 0x1FFFFF)
            {
                throw new ZeusException($"{path}.address 必须介于 0 与 2097151 之间。");
            }

            if (area == S7Area.DataBlock)
            {
                if (point.DbNumber is <= 0 or > ushort.MaxValue)
                {
                    throw new ZeusException($"{path}.db 必须介于 1 与 65535 之间。");
                }
            }
            else if (point.DbNumber != 0)
            {
                throw new ZeusException($"{path}.db 只能用于 S7 DB 区。");
            }

            if (dataType == S7DataType.Bool)
            {
                if (point.BitOffset is < 0 or > 7)
                {
                    throw new ZeusException($"{path}.bit 必须介于 0 与 7 之间。");
                }
            }
            else if (point.BitOffset != 0)
            {
                throw new ZeusException($"{path}.bit 只能用于 S7 bool 点。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ZeusConfigurationText.ValidatePointAlarms(point, path);
            if (dataType == S7DataType.Bool)
            {
                if (point.Scale is not null)
                {
                    throw new ZeusException($"{path} 是 S7 bool 点，不能配置 scale。");
                }

                if (point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                {
                    throw new ZeusException($"{path} 是 S7 bool 点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
                }
            }

            if (point.Writable && area == S7Area.Inputs)
            {
                throw new ZeusException($"{path}.area 为 I 输入区，该区域只读，不能设置 writable: true。");
            }
        }
    }

    private static void ApplyPoints(S7PointMap map, List<PointConfiguration> points)
    {
        foreach (var point in points)
        {
            var area = ParseArea(point.Area, $"point {point.Name}.area");
            var dataType = ParseDataType(point.DataType, $"point {point.Name}.dataType");
            var alarmLimits = ZeusConfigurationText.CreateAlarmLimits(point);
            if (point.Scale is { } scale)
            {
                map.ScaledPoint(point.Name, area, dataType, point.Address, scale, point.DbNumber, point.BitOffset, alarmLimits);
            }
            else
            {
                map.Point(point.Name, area, dataType, point.Address, point.DbNumber, point.BitOffset, alarmLimits);
            }

            if (point.Writable)
            {
                map.Writable(point.Name);
            }
        }
    }

    private static S7Area ParseArea(string? value, string path)
        => ZeusConfigurationText.Normalize(value) switch
        {
            "db" => S7Area.DataBlock,
            "m" => S7Area.Merkers,
            "i" => S7Area.Inputs,
            "q" => S7Area.Outputs,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 db、m、i、q。")
        };

    private static S7DataType ParseDataType(string? value, string path)
        => ZeusConfigurationText.Normalize(value) switch
        {
            "bool" => S7DataType.Bool,
            "byte" => S7DataType.Byte,
            "word" => S7DataType.Word,
            "dword" => S7DataType.DWord,
            "int" => S7DataType.Int,
            "dint" => S7DataType.DInt,
            "real" => S7DataType.Real,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 bool、byte、word、dword、int、dint、real。")
        };

    private static void ValidateByte(int value, string path)
    {
        if (value is < 0 or > byte.MaxValue)
        {
            throw new ZeusException($"{path} 必须介于 0 与 255 之间。");
        }
    }

    private static void ValidateUInt16(int value, string path)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new ZeusException($"{path} 必须介于 0 与 65535 之间。");
        }
    }
}
