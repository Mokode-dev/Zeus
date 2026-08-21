using System.Runtime.CompilerServices;

namespace Zeus;

/// <summary>
/// 程序集加载时把 Modbus JSON 绑定登记到配置核心，这样 <c>Zeus.Configuration</c> 不必引用本包。
/// </summary>
internal static class ModbusJsonBinderRegistration
{
    /// <summary>模块初始化：登记 <see cref="ModbusJsonBinder"/>。</summary>
    [ModuleInitializer]
    internal static void Register() => ZeusJsonBinders.Register(new ModbusJsonBinder());
}

/// <summary>
/// Modbus RTU/TCP/ASCII 的 JSON 设备与虚拟从站绑定。
/// </summary>
public sealed class ModbusJsonBinder : IZeusJsonBinder
{
    /// <inheritdoc />
    public IReadOnlyList<string> DeviceTypes { get; } = ["modbus-rtu", "modbus-tcp", "modbus-ascii"];

    /// <inheritdoc />
    public IReadOnlyList<string> ResponderTypes { get; } = ["modbus"];

    /// <inheritdoc />
    public void ValidateDevice(DeviceConfiguration device, string path)
    {
        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidatePoints(device.Points, path);
    }

    /// <inheritdoc />
    public void ValidateResponder(ChannelConfiguration channel, string path)
    {
        var transport = ZeusConfigurationText.Normalize(channel.Transport);
        if (transport is not ("rtu" or "tcp" or "ascii"))
        {
            throw new ZeusException($"{path}.transport「{channel.Transport}」不受支持。可选 rtu、tcp、ascii。");
        }
    }

    /// <inheritdoc />
    public void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
        => Add(device, (name, channel, unitId, timeout, points) =>
        {
            switch (CreateTransport(ZeusConfigurationText.Normalize(device.Type)))
            {
                case ModbusTransport.Tcp:
                    builder.AddModbusTcp(name, channel, unitId, timeout, points);
                    break;
                case ModbusTransport.Ascii:
                    builder.AddModbusAscii(name, channel, unitId, timeout, points);
                    break;
                default:
                    builder.AddModbusRtu(name, channel, unitId, timeout, points);
                    break;
            }
        });

    /// <inheritdoc />
    public void ApplyDevice(IZeusHost host, DeviceConfiguration device)
        => Add(device, (name, channel, unitId, timeout, points) =>
        {
            switch (CreateTransport(ZeusConfigurationText.Normalize(device.Type)))
            {
                case ModbusTransport.Tcp:
                    host.AddModbusTcp(name, channel, unitId, timeout, points);
                    break;
                case ModbusTransport.Ascii:
                    host.AddModbusAscii(name, channel, unitId, timeout, points);
                    break;
                default:
                    host.AddModbusRtu(name, channel, unitId, timeout, points);
                    break;
            }
        });

    /// <inheritdoc />
    public IVirtualResponder? CreateResponder(ChannelConfiguration channel)
    {
        if (ZeusConfigurationText.Normalize(channel.Responder) != "modbus")
        {
            return null;
        }

        return new ModbusSlaveResponder(channel.UnitId, CreateTransport(ZeusConfigurationText.Normalize(channel.Transport)));
    }

    /// <inheritdoc />
    public string DeviceFingerprint(DeviceConfiguration device)
        => string.Join('|', device.UnitId, device.TimeoutMilliseconds);

    private static void Add(
        DeviceConfiguration device,
        Action<string, string, byte, TimeSpan?, Action<ModbusPointMap>?> add)
    {
        Action<ModbusPointMap>? points = device.Points.Count == 0 ? null : map => ApplyPoints(map, device.Points);
        var timeout = device.TimeoutMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : (TimeSpan?)null;
        add(device.Name.Trim(), device.Channel.Trim(), device.UnitId, timeout, points);
    }

    private static ModbusTransport CreateTransport(string normalizedTypeOrTransport)
    {
        if (normalizedTypeOrTransport is "modbus-tcp" or "tcp")
        {
            return ModbusTransport.Tcp;
        }

        return normalizedTypeOrTransport is "modbus-ascii" or "ascii"
            ? ModbusTransport.Ascii
            : ModbusTransport.Rtu;
    }

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

            var table = ZeusConfigurationText.Normalize(point.Table);
            if (table is not ("holding" or "input" or "coil" or "discrete"))
            {
                throw new ZeusException($"{path}.table「{point.Table}」不受支持。可选 holding、input、coil、discrete。");
            }

            if (point.Address is < 0 or > ushort.MaxValue)
            {
                throw new ZeusException($"{path}.address 必须介于 0 与 65535 之间。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ZeusConfigurationText.ValidatePointAlarms(point, path);
            if ((point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                && table is "coil" or "discrete")
            {
                throw new ZeusException($"{path} 是布尔点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
            }

            if (point.Writable && table is "input" or "discrete")
            {
                throw new ZeusException($"{path} 位于只读数据区，不能设置 writable: true。");
            }

            if (point.Signed && table is not ("holding" or "input"))
            {
                throw new ZeusException($"{path}.signed 仅适用于 holding 或 input 寄存器。");
            }
        }
    }

    private static void ApplyPoints(ModbusPointMap map, List<PointConfiguration> points)
    {
        foreach (var point in points)
        {
            var table = ZeusConfigurationText.Normalize(point.Table);
            var alarmLimits = ZeusConfigurationText.CreateAlarmLimits(point);
            var scale = point.Scale ?? (point.Signed ? 1d : (double?)null);
            switch (table)
            {
                case "holding":
                    if (point.Signed)
                    {
                        map.HoldingRegister(point.Name, (ushort)point.Address, scale!.Value, signed: true, alarmLimits);
                    }
                    else if (scale is { } holdingScale)
                    {
                        map.HoldingRegister(point.Name, (ushort)point.Address, holdingScale);
                        if (alarmLimits is not null)
                        {
                            map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
                        }
                    }
                    else
                    {
                        map.HoldingRegister(point.Name, (ushort)point.Address);
                        if (alarmLimits is not null)
                        {
                            map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
                        }
                    }

                    if (point.Writable)
                    {
                        map.Writable(point.Name);
                    }

                    break;
                case "input":
                    if (point.Signed)
                    {
                        map.InputRegister(point.Name, (ushort)point.Address, scale!.Value, signed: true, alarmLimits);
                    }
                    else if (scale is { } inputScale)
                    {
                        map.InputRegister(point.Name, (ushort)point.Address, inputScale);
                        if (alarmLimits is not null)
                        {
                            map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
                        }
                    }
                    else
                    {
                        map.InputRegister(point.Name, (ushort)point.Address);
                        if (alarmLimits is not null)
                        {
                            map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
                        }
                    }

                    break;
                case "coil":
                    map.Coil(point.Name, (ushort)point.Address);
                    if (point.Writable)
                    {
                        map.Writable(point.Name);
                    }

                    break;
                case "discrete":
                    map.DiscreteInput(point.Name, (ushort)point.Address);
                    break;
            }
        }
    }
}
