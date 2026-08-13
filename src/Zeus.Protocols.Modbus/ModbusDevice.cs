namespace Zeus;

/// <summary>
/// 面向业务的 Modbus 设备：绑定通道、从站地址与传输类型，暴露读写寄存器/线圈。
/// 声明了点表后实现 <see cref="IAcquisitionSource"/>，由宿主采集循环自动轮询。
/// </summary>
public sealed class ModbusDevice : DeviceBase, IAcquisitionSource, IAsyncDisposable
{
    private readonly ModbusClient _client;
    private readonly IReadOnlyList<ModbusPointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>
    /// 创建设备。通常由 <c>AddModbusRtu</c> / <c>AddModbusTcp</c> 构造，而不是业务代码直接 new。
    /// </summary>
    /// <param name="name">设备名。</param>
    /// <param name="channel">传输通道。</param>
    /// <param name="unitId">从站地址。</param>
    /// <param name="transport">RTU 或 TCP。</param>
    /// <param name="timeout">应答超时。</param>
    /// <param name="pointMap">可选点表。为 <c>null</c> 或不含点时不参与周期采集。</param>
    public ModbusDevice(
        string name,
        IChannel channel,
        byte unitId,
        ModbusTransport transport,
        TimeSpan? timeout = null,
        ModbusPointMap? pointMap = null)
        : base(name, channel)
    {
        UnitId = unitId;
        Transport = transport;
        _client = new ModbusClient(channel, transport, timeout);
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs
            .Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits))
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

    /// <summary>写单个保持寄存器。</summary>
    public Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default)
        => _client.WriteSingleRegisterAsync(UnitId, address, value, cancellationToken);

    /// <summary>写多个保持寄存器。</summary>
    public Task WriteMultipleRegistersAsync(ushort address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteMultipleRegistersAsync(UnitId, address, values, cancellationToken);

    /// <summary>按 AND / OR 掩码修改单个保持寄存器（功能码 0x16）。</summary>
    public Task MaskWriteRegisterAsync(ushort address, ushort andMask, ushort orMask, CancellationToken cancellationToken = default)
        => _client.MaskWriteRegisterAsync(UnitId, address, andMask, orMask, cancellationToken);

    /// <summary>写单个线圈。</summary>
    public Task WriteSingleCoilAsync(ushort address, bool value, CancellationToken cancellationToken = default)
        => _client.WriteSingleCoilAsync(UnitId, address, value, cancellationToken);

    /// <summary>写多个线圈（功能码 0x0F）。</summary>
    public Task WriteMultipleCoilsAsync(ushort address, IReadOnlyList<bool> values, CancellationToken cancellationToken = default)
        => _client.WriteMultipleCoilsAsync(UnitId, address, values, cancellationToken);

    /// <inheritdoc />
    public IReadOnlyList<PointDefinition> Points => _points;

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
