using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>
/// 程序集加载时登记 Mitsubishi MC 的 JSON 绑定。
/// </summary>
internal static class McJsonBinderRegistration
{
    /// <summary>模块初始化：登记 <see cref="McJsonBinder"/>。</summary>
    [ModuleInitializer]
    internal static void Register() => ZeusJsonBinders.Register(new McJsonBinder());
}

/// <summary>
/// Mitsubishi MC 的 JSON 设备与虚拟从站绑定。
/// </summary>
public sealed class McJsonBinder : IZeusJsonBinder
{
    /// <inheritdoc />
    public IReadOnlyList<string> DeviceTypes { get; } = ["mitsubishi-mc"];

    /// <inheritdoc />
    public IReadOnlyList<string> ResponderTypes { get; } = ["mc"];

    /// <inheritdoc />
    public void ValidateDevice(DeviceConfiguration device, string path)
    {
        var frameType = ParseFrameType(device.FrameType, $"{path}.frameType");
        ParseEncoding(device.Encoding, $"{path}.encoding");
        ValidateByte(device.NetworkNumber, $"{path}.networkNumber");
        ValidateByte(device.PcNumber, $"{path}.pcNumber");
        ValidateUInt16(device.IoNumber, $"{path}.ioNumber");
        ValidateByte(device.StationNumber, $"{path}.stationNumber");
        ValidateUInt16(device.MonitoringTimer, $"{path}.monitoringTimer");
        ValidateUInt16(device.SerialNumber, $"{path}.serialNumber");
        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidatePoints(device.Points, path, frameType);
    }

    /// <inheritdoc />
    public void ValidateResponder(ChannelConfiguration channel, string path)
    {
    }

    /// <inheritdoc />
    public void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
    {
        var timeout = device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : (TimeSpan?)null;
        Action<McPointMap>? points = device.Points.Count == 0 ? null : map => ApplyPoints(map, device.Points);
        builder.AddMitsubishiMc(device.Name.Trim(), device.Channel.Trim(), CreateOptions(device), timeout, points);
    }

    /// <inheritdoc />
    public void ApplyDevice(IZeusHost host, DeviceConfiguration device)
    {
        var timeout = device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : (TimeSpan?)null;
        Action<McPointMap>? points = device.Points.Count == 0 ? null : map => ApplyPoints(map, device.Points);
        host.AddMitsubishiMc(device.Name.Trim(), device.Channel.Trim(), CreateOptions(device), timeout, points);
    }

    /// <inheritdoc />
    public IVirtualResponder? CreateResponder(ChannelConfiguration channel)
        => ZeusConfigurationText.Normalize(channel.Responder) == "mc" ? new McSlaveResponder() : null;

    /// <inheritdoc />
    public string DeviceFingerprint(DeviceConfiguration device)
        => string.Join('|',
            ZeusConfigurationText.Normalize(device.FrameType),
            ZeusConfigurationText.Normalize(device.Encoding),
            device.TimeoutMilliseconds,
            device.NetworkNumber,
            device.PcNumber,
            device.IoNumber,
            device.StationNumber,
            device.MonitoringTimer,
            device.SerialNumber);

    private static McOptions CreateOptions(DeviceConfiguration device)
        => new()
        {
            FrameType = ParseFrameType(device.FrameType, "device.frameType"),
            DataEncoding = ParseEncoding(device.Encoding, "device.encoding"),
            NetworkNumber = (byte)device.NetworkNumber,
            PcNumber = (byte)device.PcNumber,
            IoNumber = (ushort)device.IoNumber,
            StationNumber = (byte)device.StationNumber,
            MonitoringTimer = (ushort)device.MonitoringTimer,
            SerialNumber = (ushort)device.SerialNumber
        };

    private static void ValidatePoints(List<PointConfiguration> points, string devicePath, McFrameType frameType)
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

            if (string.IsNullOrWhiteSpace(point.DeviceCode))
            {
                throw new ZeusException($"{path}.deviceCode 必须指定。Mitsubishi MC 可选 D、M、X、Y、W、R、ZR。");
            }

            var deviceCode = ParseDeviceCode(point.DeviceCode, $"{path}.deviceCode");
            if (frameType == McFrameType.Frame1E && deviceCode == McDeviceCode.ExtendedFileRegister)
            {
                throw new ZeusException($"{path}.deviceCode 为 ZR，但 MC 1E 帧不支持 ZR。请改用 3e/4e，或移除该点。");
            }

            if (point.Address is < 0 or > 0xFFFFFF)
            {
                throw new ZeusException($"{path}.address 必须介于 0 与 16777215 之间。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ZeusConfigurationText.ValidatePointAlarms(point, path);
            var isBit = deviceCode is McDeviceCode.InternalRelay or McDeviceCode.InputRelay or McDeviceCode.OutputRelay;
            if (point.Scale is not null && isBit)
            {
                throw new ZeusException($"{path} 是 MC 位软元件，不能配置 scale。");
            }

            if ((point.LowAlarmLimit is not null || point.HighAlarmLimit is not null) && isBit)
            {
                throw new ZeusException($"{path} 是 MC 位软元件，不能配置 lowAlarmLimit 或 highAlarmLimit。");
            }

            if (point.Writable && deviceCode == McDeviceCode.InputRelay)
            {
                throw new ZeusException($"{path}.deviceCode 为 X 输入继电器，该软元件只读，不能设置 writable: true。");
            }
        }
    }

    private static void ApplyPoints(McPointMap map, List<PointConfiguration> points)
    {
        foreach (var point in points)
        {
            var deviceCode = ParseDeviceCode(point.DeviceCode, $"point {point.Name}.deviceCode");
            var alarmLimits = ZeusConfigurationText.CreateAlarmLimits(point);
            var isWord = deviceCode is McDeviceCode.DataRegister
                or McDeviceCode.LinkRegister
                or McDeviceCode.FileRegister
                or McDeviceCode.ExtendedFileRegister;
            if (isWord)
            {
                if (point.Scale is { } scale)
                {
                    map.Word(point.Name, deviceCode, point.Address, scale);
                }
                else
                {
                    map.Word(point.Name, deviceCode, point.Address);
                }

                if (alarmLimits is not null)
                {
                    map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
                }
            }
            else
            {
                map.Bit(point.Name, deviceCode, point.Address);
            }

            if (point.Writable)
            {
                map.Writable(point.Name);
            }
        }
    }

    private static McFrameType ParseFrameType(string? value, string path)
        => ZeusConfigurationText.Normalize(value) switch
        {
            "1e" => McFrameType.Frame1E,
            "3e" or "" => McFrameType.Frame3E,
            "4e" => McFrameType.Frame4E,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 1e、3e、4e。")
        };

    private static McDataEncoding ParseEncoding(string? value, string path)
        => ZeusConfigurationText.Normalize(value) switch
        {
            "binary" or "" => McDataEncoding.Binary,
            "ascii" => McDataEncoding.Ascii,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 binary、ascii。")
        };

    private static McDeviceCode ParseDeviceCode(string? value, string path)
        => ZeusConfigurationText.Normalize(value) switch
        {
            "d" => McDeviceCode.DataRegister,
            "m" => McDeviceCode.InternalRelay,
            "x" => McDeviceCode.InputRelay,
            "y" => McDeviceCode.OutputRelay,
            "w" => McDeviceCode.LinkRegister,
            "r" => McDeviceCode.FileRegister,
            "zr" => McDeviceCode.ExtendedFileRegister,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 D、M、X、Y、W、R、ZR。")
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
