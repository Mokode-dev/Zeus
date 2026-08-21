using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 面向业务的 Modbus 设备：绑定通道、从站地址与传输类型，暴露读写寄存器/线圈。
/// 声明了点表后实现 <see cref="IAcquisitionSource"/>，由宿主采集循环自动轮询；
/// 标为可写的点实现 <see cref="IPointWriter"/>，可通过点表按名称下发。
/// </summary>
public sealed class ModbusDevice : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly ModbusClient _client;
    private readonly IReadOnlyList<ModbusPointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>
    /// 创建设备。通常由 <c>AddModbusRtu</c> / <c>AddModbusTcp</c> / <c>AddModbusAscii</c> 构造，而不是业务代码直接 new。
    /// </summary>
    /// <param name="name">设备名。</param>
    /// <param name="channel">传输通道。</param>
    /// <param name="unitId">从站地址。</param>
    /// <param name="transport">RTU、TCP 或 ASCII。</param>
    /// <param name="timeout">应答超时。</param>
    /// <param name="pointMap">可选点表。为 <c>null</c> 或不含点时不参与周期采集。</param>
    /// <param name="logger">诊断日志。宿主注册时自动注入。</param>
    public ModbusDevice(
        string name,
        IChannel channel,
        byte unitId,
        ModbusTransport transport,
        TimeSpan? timeout = null,
        ModbusPointMap? pointMap = null,
        ILogger<ModbusDevice>? logger = null)
        : base(name, channel, logger)
    {
        UnitId = unitId;
        Transport = transport;
        _client = new ModbusClient(channel, transport, timeout);
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs
            .Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable))
            .ToArray();
    }

    /// <summary>从站/单元标识。</summary>
    public byte UnitId { get; }

    /// <summary>线上封装。</summary>
    public ModbusTransport Transport { get; }

    /// <summary>读保持寄存器。</summary>
    public Task<ushort[]> ReadHoldingRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default)
        => _client.ReadHoldingRegistersAsync(UnitId, address, quantity, cancellationToken);

    /// <summary>读输入寄存器。</summary>
    public Task<ushort[]> ReadInputRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default)
        => _client.ReadInputRegistersAsync(UnitId, address, quantity, cancellationToken);

    /// <summary>读线圈。</summary>
    public Task<bool[]> ReadCoilsAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default)
        => _client.ReadCoilsAsync(UnitId, address, quantity, cancellationToken);

    /// <summary>读离散输入。</summary>
    public Task<bool[]> ReadDiscreteInputsAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default)
        => _client.ReadDiscreteInputsAsync(UnitId, address, quantity, cancellationToken);

    /// <summary>读异常状态（功能码 0x07）。</summary>
    public Task<byte> ReadExceptionStatusAsync(CancellationToken cancellationToken = default)
        => _client.ReadExceptionStatusAsync(UnitId, cancellationToken);

    /// <summary>执行诊断回显（功能码 0x08，子功能 0x0000）。</summary>
    public Task<ushort> DiagnosticsReturnQueryDataAsync(ushort data, CancellationToken cancellationToken = default)
        => _client.DiagnosticsReturnQueryDataAsync(UnitId, data, cancellationToken);

    /// <summary>报告服务器 ID（功能码 0x11）。</summary>
    public Task<ModbusServerId> ReportServerIdAsync(CancellationToken cancellationToken = default)
        => _client.ReportServerIdAsync(UnitId, cancellationToken);

    /// <summary>写单个保持寄存器。</summary>
    public Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default)
        => _client.WriteSingleRegisterAsync(UnitId, address, value, cancellationToken);

    /// <summary>写多个保持寄存器。</summary>
    public Task WriteMultipleRegistersAsync(ushort address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteMultipleRegistersAsync(UnitId, address, values, cancellationToken);

    /// <summary>按 AND / OR 掩码修改单个保持寄存器（功能码 0x16）。</summary>
    public Task MaskWriteRegisterAsync(ushort address, ushort andMask, ushort orMask, CancellationToken cancellationToken = default)
        => _client.MaskWriteRegisterAsync(UnitId, address, andMask, orMask, cancellationToken);

    /// <summary>读写多个保持寄存器（功能码 0x17）。写操作先执行，再返回读取区间。</summary>
    public Task<ushort[]> ReadWriteMultipleRegistersAsync(
        ushort readAddress,
        ushort readQuantity,
        ushort writeAddress,
        IReadOnlyList<ushort> writeValues,
        CancellationToken cancellationToken = default)
        => _client.ReadWriteMultipleRegistersAsync(UnitId, readAddress, readQuantity, writeAddress, writeValues, cancellationToken);

    /// <summary>读设备识别（功能码 0x2B / MEI 0x0E）。</summary>
    public Task<ModbusDeviceIdentification> ReadDeviceIdentificationAsync(
        byte deviceIdCode = 1,
        byte objectId = 0,
        CancellationToken cancellationToken = default)
        => _client.ReadDeviceIdentificationAsync(UnitId, deviceIdCode, objectId, cancellationToken);

    /// <summary>读文件记录（功能码 0x14）。</summary>
    public Task<ushort[]> ReadFileRecordAsync(
        ushort fileNumber,
        ushort recordNumber,
        ushort recordLength,
        CancellationToken cancellationToken = default)
        => _client.ReadFileRecordAsync(UnitId, fileNumber, recordNumber, recordLength, cancellationToken);

    /// <summary>写文件记录（功能码 0x15）。</summary>
    public Task WriteFileRecordAsync(
        ushort fileNumber,
        ushort recordNumber,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
        => _client.WriteFileRecordAsync(UnitId, fileNumber, recordNumber, values, cancellationToken);

    /// <summary>写单个线圈。</summary>
    public Task WriteSingleCoilAsync(ushort address, bool value, CancellationToken cancellationToken = default)
        => _client.WriteSingleCoilAsync(UnitId, address, value, cancellationToken);

    /// <summary>写多个线圈（功能码 0x0F）。</summary>
    public Task WriteMultipleCoilsAsync(ushort address, IReadOnlyList<bool> values, CancellationToken cancellationToken = default)
        => _client.WriteMultipleCoilsAsync(UnitId, address, values, cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <inheritdoc />
    public async Task WriteAsync(
        string pointName,
        object value,
        IPointTableWriter table,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(table);
        var spec = FindSpec(pointName);
        var qualified = Name + "." + spec.Name;
        try
        {
            var published = await WriteSpecAsync(spec, value, cancellationToken).ConfigureAwait(false);
            table.Publish(qualified, published);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogWriteFailed(ex, spec.Name);
            table.PublishError(qualified, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PollAsync(IPointTableWriter table, CancellationToken cancellationToken = default)
    {
        foreach (var group in GroupConsecutive(_specs))
        {
            try
            {
                await PublishGroupAsync(table, group, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogAcquisitionFailed(ex, group[0].Name);
                foreach (var spec in group)
                {
                    table.PublishError(Name + "." + spec.Name, ex.Message);
                }
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private async Task PublishGroupAsync(
        IPointTableWriter table,
        IReadOnlyList<ModbusPointSpec> group,
        CancellationToken cancellationToken)
    {
        var first = group[0];
        var quantity = (ushort)(group[^1].Address - first.Address + 1);
        if (first.Table is ModbusTable.HoldingRegister or ModbusTable.InputRegister)
        {
            var values = first.Table == ModbusTable.HoldingRegister
                ? await ReadHoldingRegistersAsync(first.Address, quantity, cancellationToken).ConfigureAwait(false)
                : await ReadInputRegistersAsync(first.Address, quantity, cancellationToken).ConfigureAwait(false);
            foreach (var spec in group)
            {
                var raw = values[spec.Address - first.Address];
                table.Publish(Name + "." + spec.Name, spec.Convert is null ? raw : spec.Convert(raw));
            }

            return;
        }

        var bits = first.Table == ModbusTable.Coil
            ? await ReadCoilsAsync(first.Address, quantity, cancellationToken).ConfigureAwait(false)
            : await ReadDiscreteInputsAsync(first.Address, quantity, cancellationToken).ConfigureAwait(false);
        foreach (var spec in group)
        {
            table.Publish(Name + "." + spec.Name, bits[spec.Address - first.Address]);
        }
    }

    /// <summary>
    /// 按点描述把工程值写到从站，并返回应写入点表的值（与采集换算一致）。
    /// </summary>
    private async Task<object> WriteSpecAsync(ModbusPointSpec spec, object value, CancellationToken cancellationToken)
    {
        if (!spec.Writable)
        {
            throw new ZeusException($"点 {Name}.{spec.Name} 未标为可写。");
        }

        if (spec.Table == ModbusTable.Coil)
        {
            var bit = ConvertToBoolean(value, spec.Name);
            await WriteSingleCoilAsync(spec.Address, bit, cancellationToken).ConfigureAwait(false);
            return bit;
        }

        if (spec.Table != ModbusTable.HoldingRegister)
        {
            throw new ZeusException($"点 {Name}.{spec.Name} 位于只读数据区，不能写回。");
        }

        var raw = ConvertToRegister(spec, value);
        await WriteSingleRegisterAsync(spec.Address, raw, cancellationToken).ConfigureAwait(false);
        return spec.Convert is null ? raw : spec.Convert(raw);
    }

    /// <summary>
    /// 按短名查找点描述。
    /// </summary>
    private ModbusPointSpec FindSpec(string pointName)
    {
        if (string.IsNullOrWhiteSpace(pointName))
        {
            throw new ZeusException("写回点名不能为空。");
        }

        var key = pointName.Trim();
        foreach (var spec in _specs)
        {
            if (string.Equals(spec.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                return spec;
            }
        }

        throw new ZeusException($"设备 {Name} 上找不到点 {key}。");
    }

    /// <summary>
    /// 把工程值换成保持寄存器原始值。带 scale 时先反除再四舍五入。
    /// </summary>
    private ushort ConvertToRegister(ModbusPointSpec spec, object value)
    {
        if (spec.Scale is { } scale)
        {
            var engineering = ConvertToDouble(value, spec.Name);
            var raw = engineering / scale;
            if (!double.IsFinite(raw))
            {
                throw new ZeusException($"点 {Name}.{spec.Name} 的工程值 {engineering} 无法按 scale={scale} 反算。");
            }

            var rounded = Math.Round(raw, MidpointRounding.AwayFromZero);
            if (spec.Signed)
            {
                if (rounded is < short.MinValue or > short.MaxValue)
                {
                    throw new ZeusException(
                        $"点 {Name}.{spec.Name} 的工程值 {engineering} 反算为 {rounded}，超出有符号寄存器 {short.MinValue}–{short.MaxValue}。");
                }

                return unchecked((ushort)(short)rounded);
            }

            if (rounded is < ushort.MinValue or > ushort.MaxValue)
            {
                throw new ZeusException(
                    $"点 {Name}.{spec.Name} 的工程值 {engineering} 反算为 {rounded}，超出保持寄存器 0–65535。");
            }

            return (ushort)rounded;
        }

        return ConvertToUInt16(value, spec.Name);
    }

    private static bool ConvertToBoolean(object value, string pointName)
    {
        if (value is bool bit)
        {
            return bit;
        }

        if (value is string text)
        {
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return number != 0;
            }
        }

        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new ZeusException($"点 {pointName} 需要布尔值，无法把 {value}（{value.GetType().Name}）写回。", ex);
        }
    }

    private static double ConvertToDouble(object value, string pointName)
    {
        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new ZeusException($"点 {pointName} 需要数值，无法把 {value}（{value.GetType().Name}）写回。", ex);
        }
    }

    private static ushort ConvertToUInt16(object value, string pointName)
    {
        try
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var rounded = Math.Round(number, MidpointRounding.AwayFromZero);
            if (rounded is < ushort.MinValue or > ushort.MaxValue)
            {
                throw new ZeusException($"点 {pointName} 的值 {value} 超出保持寄存器 0–65535。");
            }

            return (ushort)rounded;
        }
        catch (ZeusException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ZeusException($"点 {pointName} 需要 0–65535 的整数，无法把 {value}（{value.GetType().Name}）写回。", ex);
        }
    }

    /// <summary>
    /// 把同一数据区且地址连续的点合成一组，减少总线往返。
    /// </summary>
    private static List<List<ModbusPointSpec>> GroupConsecutive(IReadOnlyList<ModbusPointSpec> specs)
    {
        var groups = new List<List<ModbusPointSpec>>();
        foreach (var spec in specs.OrderBy(item => item.Table).ThenBy(item => item.Address))
        {
            if (groups.Count > 0)
            {
                var current = groups[^1];
                var last = current[^1];
                if (last.Table == spec.Table && spec.Address == last.Address + 1)
                {
                    current.Add(spec);
                    continue;
                }
            }

            groups.Add([spec]);
        }

        return groups;
    }
}
