namespace Zeus;

/// <summary>
/// 声明一台 Modbus 设备上要周期采集的点。连续地址会在采集时自动合并为一次读取。
/// </summary>
public sealed class ModbusPointMap
{
    private readonly List<ModbusPointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<ModbusPointSpec> Points => _points;

    /// <summary>
    /// 声明一个保持寄存器点，值为原始 <see cref="ushort"/>。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    public ModbusPointMap HoldingRegister(string name, ushort address)
        => AddRegister(name, ModbusTable.HoldingRegister, address, PointValueKind.UInt16, null);

    /// <summary>
    /// 声明一个保持寄存器点，并用换算函数得到工程值。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="convert">例如 <c>raw =&gt; raw * 0.1</c>。</param>
    public ModbusPointMap HoldingRegister(string name, ushort address, Func<ushort, double> convert)
        => AddRegister(name, ModbusTable.HoldingRegister, address, PointValueKind.Double, raw => convert(raw));

    /// <summary>
    /// 声明一个输入寄存器点。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    public ModbusPointMap InputRegister(string name, ushort address)
        => AddRegister(name, ModbusTable.InputRegister, address, PointValueKind.UInt16, null);

    /// <summary>
    /// 声明一个带换算的输入寄存器点。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="convert">工程值换算。</param>
    public ModbusPointMap InputRegister(string name, ushort address, Func<ushort, double> convert)
        => AddRegister(name, ModbusTable.InputRegister, address, PointValueKind.Double, raw => convert(raw));

    /// <summary>
    /// 声明一个线圈点。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    public ModbusPointMap Coil(string name, ushort address)
        => AddBit(name, ModbusTable.Coil, address);

    /// <summary>
    /// 声明一个离散输入点。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    public ModbusPointMap DiscreteInput(string name, ushort address)
        => AddBit(name, ModbusTable.DiscreteInput, address);

    private ModbusPointMap AddRegister(
        string name,
        ModbusTable table,
        ushort address,
        PointValueKind kind,
        Func<ushort, object>? convert)
    {
        Add(new ModbusPointSpec(Normalize(name), table, address, kind, convert));
        return this;
    }

    private ModbusPointMap AddBit(string name, ModbusTable table, ushort address)
    {
        Add(new ModbusPointSpec(Normalize(name), table, address, PointValueKind.Boolean, null));
        return this;
    }

    private void Add(ModbusPointSpec spec)
    {
        if (!_names.Add(spec.Name))
        {
            throw new ZeusException($"同一台 Modbus 设备上点名 {spec.Name} 重复。");
        }

        _points.Add(spec);
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("Modbus 点名不能为空。");
        }

        return name.Trim();
    }
}
