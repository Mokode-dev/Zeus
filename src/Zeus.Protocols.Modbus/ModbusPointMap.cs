namespace Zeus;

/// <summary>
/// 声明一台 Modbus 设备上要周期采集的点。连续地址会在采集时自动合并为一次读取。
/// 保持寄存器与线圈可再调用 <see cref="Writable"/>，以便按点名写回。
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
        => AddRegister(name, ModbusTable.HoldingRegister, address, PointValueKind.UInt16, null, null);

    /// <summary>
    /// 声明一个带报警限的保持寄存器点，值为原始 <see cref="ushort"/>。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="alarmLimits">报警限。</param>
    public ModbusPointMap HoldingRegister(string name, ushort address, PointAlarmLimits alarmLimits)
        => AddRegister(name, ModbusTable.HoldingRegister, address, PointValueKind.UInt16, null, alarmLimits);

    /// <summary>
    /// 声明一个保持寄存器点，并用换算函数得到工程值。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="convert">例如 <c>raw =&gt; raw * 0.1</c>。</param>
    public ModbusPointMap HoldingRegister(string name, ushort address, Func<ushort, double> convert)
        => AddRegister(name, ModbusTable.HoldingRegister, address, PointValueKind.Double, raw => convert(raw), null);

    /// <summary>
    /// 声明一个带报警限的保持寄存器点，并用换算函数得到工程值。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="convert">例如 <c>raw =&gt; raw * 0.1</c>。</param>
    /// <param name="alarmLimits">报警限，按换算后的工程值判断。</param>
    public ModbusPointMap HoldingRegister(
        string name,
        ushort address,
        Func<ushort, double> convert,
        PointAlarmLimits alarmLimits)
        => AddRegister(name, ModbusTable.HoldingRegister, address, PointValueKind.Double, raw => convert(raw), alarmLimits);

    /// <summary>
    /// 声明一个输入寄存器点。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    public ModbusPointMap InputRegister(string name, ushort address)
        => AddRegister(name, ModbusTable.InputRegister, address, PointValueKind.UInt16, null, null);

    /// <summary>
    /// 声明一个带报警限的输入寄存器点。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="alarmLimits">报警限。</param>
    public ModbusPointMap InputRegister(string name, ushort address, PointAlarmLimits alarmLimits)
        => AddRegister(name, ModbusTable.InputRegister, address, PointValueKind.UInt16, null, alarmLimits);

    /// <summary>
    /// 声明一个带换算的输入寄存器点。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="convert">工程值换算。</param>
    public ModbusPointMap InputRegister(string name, ushort address, Func<ushort, double> convert)
        => AddRegister(name, ModbusTable.InputRegister, address, PointValueKind.Double, raw => convert(raw), null);

    /// <summary>
    /// 声明一个带报警限的输入寄存器点，并用换算函数得到工程值。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="convert">工程值换算。</param>
    /// <param name="alarmLimits">报警限，按换算后的工程值判断。</param>
    public ModbusPointMap InputRegister(
        string name,
        ushort address,
        Func<ushort, double> convert,
        PointAlarmLimits alarmLimits)
        => AddRegister(name, ModbusTable.InputRegister, address, PointValueKind.Double, raw => convert(raw), alarmLimits);

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

    /// <summary>
    /// 声明一个带线性换算的保持寄存器点。写回时会按同一系数反算。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="scale">工程值 = 原始值 × 该系数，必须大于 0。</param>
    public ModbusPointMap HoldingRegister(string name, ushort address, double scale)
        => AddScaledRegister(name, ModbusTable.HoldingRegister, address, scale, null);

    /// <summary>
    /// 声明一个带线性换算和报警限的保持寄存器点。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="scale">工程值 = 原始值 × 该系数，必须大于 0。</param>
    /// <param name="alarmLimits">报警限，按换算后的工程值判断。</param>
    public ModbusPointMap HoldingRegister(string name, ushort address, double scale, PointAlarmLimits alarmLimits)
        => AddScaledRegister(name, ModbusTable.HoldingRegister, address, scale, alarmLimits);

    /// <summary>
    /// 声明一个按有符号 Int16 解释的保持寄存器点，并可带线性换算。
    /// 放电电流等以补码存放的量应使用本重载，而不是无符号 <c>scale</c>。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="scale">工程值 = 有符号原始值 × 该系数，必须大于 0。</param>
    /// <param name="signed">为 <c>true</c> 时按 Int16 补码解释寄存器。</param>
    /// <param name="alarmLimits">可选报警限，按换算后的工程值判断。</param>
    public ModbusPointMap HoldingRegister(
        string name,
        ushort address,
        double scale,
        bool signed,
        PointAlarmLimits? alarmLimits = null)
        => signed
            ? AddSignedRegister(name, ModbusTable.HoldingRegister, address, scale, alarmLimits)
            : alarmLimits is null
                ? AddScaledRegister(name, ModbusTable.HoldingRegister, address, scale, null)
                : AddScaledRegister(name, ModbusTable.HoldingRegister, address, scale, alarmLimits);

    /// <summary>
    /// 声明一个带线性换算的输入寄存器点。输入区只读，不能再标为可写。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="scale">工程值 = 原始值 × 该系数，必须大于 0。</param>
    public ModbusPointMap InputRegister(string name, ushort address, double scale)
        => AddScaledRegister(name, ModbusTable.InputRegister, address, scale, null);

    /// <summary>
    /// 声明一个带线性换算和报警限的输入寄存器点。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="address">0 基地址。</param>
    /// <param name="scale">工程值 = 原始值 × 该系数，必须大于 0。</param>
    /// <param name="alarmLimits">报警限，按换算后的工程值判断。</param>
    public ModbusPointMap InputRegister(string name, ushort address, double scale, PointAlarmLimits alarmLimits)
        => AddScaledRegister(name, ModbusTable.InputRegister, address, scale, alarmLimits);

    /// <summary>
    /// 声明一个按有符号 Int16 解释的输入寄存器点。
    /// </summary>
    public ModbusPointMap InputRegister(
        string name,
        ushort address,
        double scale,
        bool signed,
        PointAlarmLimits? alarmLimits = null)
        => signed
            ? AddSignedRegister(name, ModbusTable.InputRegister, address, scale, alarmLimits)
            : alarmLimits is null
                ? AddScaledRegister(name, ModbusTable.InputRegister, address, scale, null)
                : AddScaledRegister(name, ModbusTable.InputRegister, address, scale, alarmLimits);

    /// <summary>
    /// 把已经声明的点标为可写，之后可通过 <see cref="IPointTable.WriteAsync"/> 按名称下发。
    /// 输入寄存器和离散输入不能写；使用自定义换算函数、未提供 <c>scale</c> 的点也无法自动反算。
    /// </summary>
    /// <param name="name">点名。</param>
    public ModbusPointMap Writable(string name)
    {
        var normalized = Normalize(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (!string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (point.Table is ModbusTable.InputRegister or ModbusTable.DiscreteInput)
            {
                throw new ZeusException(
                    $"点 {normalized} 位于 {DescribeTable(point.Table)}，该数据区只读，不能标为可写。");
            }

            if (point.Convert is not null && point.Scale is null)
            {
                throw new ZeusException(
                    $"点 {normalized} 使用了自定义换算函数，无法把工程值反算为寄存器。请改用 HoldingRegister(\"{normalized}\", address, scale) 再调用 Writable。");
            }

            _points[i] = point.WithWritable(true);
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再标为可写。");
    }

    /// <summary>
    /// 为已经声明的数值点设置或替换报警限。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="low">低报阈值。</param>
    /// <param name="high">高报阈值。</param>
    public ModbusPointMap WithAlarmLimits(string name, double? low = null, double? high = null)
    {
        var normalized = Normalize(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (!string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (point.Kind == PointValueKind.Boolean)
            {
                throw new ZeusException($"点 {normalized} 是布尔点，不能配置数值报警限。");
            }

            _points[i] = point.WithAlarmLimits(new PointAlarmLimits(low, high));
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再配置报警限。");
    }

    private ModbusPointMap AddRegister(
        string name,
        ModbusTable table,
        ushort address,
        PointValueKind kind,
        Func<ushort, object>? convert,
        PointAlarmLimits? alarmLimits)
    {
        Add(new ModbusPointSpec(Normalize(name), table, address, kind, convert, alarmLimits));
        return this;
    }

    private ModbusPointMap AddScaledRegister(
        string name,
        ModbusTable table,
        ushort address,
        double scale,
        PointAlarmLimits? alarmLimits)
    {
        if (scale <= 0 || !double.IsFinite(scale))
        {
            throw new ZeusException($"点 {Normalize(name)} 的 scale 必须是大于 0 的有限数值。");
        }

        Add(new ModbusPointSpec(
            Normalize(name),
            table,
            address,
            PointValueKind.Double,
            raw => raw * scale,
            alarmLimits,
            writable: false,
            scale: scale));
        return this;
    }

    /// <summary>
    /// 按 Int16 补码解释寄存器，再乘以线性系数。写回时限制在有符号 16 位范围。
    /// </summary>
    private ModbusPointMap AddSignedRegister(
        string name,
        ModbusTable table,
        ushort address,
        double scale,
        PointAlarmLimits? alarmLimits)
    {
        if (scale <= 0 || !double.IsFinite(scale))
        {
            throw new ZeusException($"点 {Normalize(name)} 的 scale 必须是大于 0 的有限数值。");
        }

        Add(new ModbusPointSpec(
            Normalize(name),
            table,
            address,
            PointValueKind.Double,
            raw => unchecked((short)raw) * scale,
            alarmLimits,
            writable: false,
            scale: scale,
            signed: true));
        return this;
    }

    private ModbusPointMap AddBit(string name, ModbusTable table, ushort address)
    {
        Add(new ModbusPointSpec(Normalize(name), table, address, PointValueKind.Boolean, null, null));
        return this;
    }

    private static string DescribeTable(ModbusTable table)
        => table switch
        {
            ModbusTable.HoldingRegister => "保持寄存器",
            ModbusTable.InputRegister => "输入寄存器",
            ModbusTable.Coil => "线圈",
            ModbusTable.DiscreteInput => "离散输入",
            _ => table.ToString()
        };

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
