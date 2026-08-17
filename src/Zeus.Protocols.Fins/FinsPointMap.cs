namespace Zeus;

/// <summary>
/// 声明一台 Omron FINS 设备上要周期采集的点。连续同区字点会在采集时自动合并读取。
/// </summary>
public sealed class FinsPointMap
{
    private readonly List<FinsPointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<FinsPointSpec> Points => _points;

    /// <summary>声明通用位点。</summary>
    public FinsPointMap Bit(string name, FinsMemoryAreaCode area, ushort address, byte bitOffset = 0)
    {
        var normalized = Normalize(name);
        EnsureBitArea(area, normalized);
        EnsureBitOffset(bitOffset, normalized);
        Add(new FinsPointSpec(normalized, area, address, bitOffset, FinsDataType.Bit, PointValueKind.Boolean, null));
        return this;
    }

    /// <summary>声明通用字点，值为原始 <see cref="ushort"/>。</summary>
    public FinsPointMap Word(string name, FinsMemoryAreaCode area, ushort address)
        => AddWord(name, area, address, FinsDataType.Word, null, null);

    /// <summary>声明带报警限的通用字点，值为原始 <see cref="ushort"/>。</summary>
    public FinsPointMap Word(string name, FinsMemoryAreaCode area, ushort address, PointAlarmLimits alarmLimits)
        => AddWord(name, area, address, FinsDataType.Word, null, alarmLimits);

    /// <summary>声明带线性换算的通用字点。</summary>
    public FinsPointMap Word(string name, FinsMemoryAreaCode area, ushort address, double scale)
        => AddWord(name, area, address, FinsDataType.Word, scale, null);

    /// <summary>声明带线性换算和报警限的通用字点。</summary>
    public FinsPointMap Word(string name, FinsMemoryAreaCode area, ushort address, double scale, PointAlarmLimits alarmLimits)
        => AddWord(name, area, address, FinsDataType.Word, scale, alarmLimits);

    /// <summary>声明 16 位有符号整数点。</summary>
    public FinsPointMap Int16(string name, FinsMemoryAreaCode area, ushort address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, address, FinsDataType.Int16, scale, alarmLimits);

    /// <summary>声明 32 位无符号整数点，占两个连续字。</summary>
    public FinsPointMap UInt32(string name, FinsMemoryAreaCode area, ushort address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, address, FinsDataType.UInt32, scale, alarmLimits);

    /// <summary>声明 32 位有符号整数点，占两个连续字。</summary>
    public FinsPointMap Int32(string name, FinsMemoryAreaCode area, ushort address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, address, FinsDataType.Int32, scale, alarmLimits);

    /// <summary>声明 32 位浮点点，占两个连续字。</summary>
    public FinsPointMap Real(string name, FinsMemoryAreaCode area, ushort address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, address, FinsDataType.Real, scale, alarmLimits);

    /// <summary>声明 DM 字点。</summary>
    public FinsPointMap DmWord(string name, ushort address) => Word(name, FinsMemoryAreaCode.DataMemoryWord, address);

    /// <summary>声明带线性换算的 DM 字点。</summary>
    public FinsPointMap DmWord(string name, ushort address, double scale) => Word(name, FinsMemoryAreaCode.DataMemoryWord, address, scale);

    /// <summary>声明 DM 位点。</summary>
    public FinsPointMap DmBit(string name, ushort address, byte bitOffset) => Bit(name, FinsMemoryAreaCode.DataMemoryBit, address, bitOffset);

    /// <summary>声明 CIO 字点。</summary>
    public FinsPointMap CioWord(string name, ushort address) => Word(name, FinsMemoryAreaCode.CioWord, address);

    /// <summary>声明 CIO 位点。</summary>
    public FinsPointMap CioBit(string name, ushort address, byte bitOffset) => Bit(name, FinsMemoryAreaCode.CioBit, address, bitOffset);

    /// <summary>声明 WR 字点。</summary>
    public FinsPointMap WorkWord(string name, ushort address) => Word(name, FinsMemoryAreaCode.WorkWord, address);

    /// <summary>声明 WR 位点。</summary>
    public FinsPointMap WorkBit(string name, ushort address, byte bitOffset) => Bit(name, FinsMemoryAreaCode.WorkBit, address, bitOffset);

    /// <summary>声明 HR 字点。</summary>
    public FinsPointMap HoldingWord(string name, ushort address) => Word(name, FinsMemoryAreaCode.HoldingWord, address);

    /// <summary>声明 HR 位点。</summary>
    public FinsPointMap HoldingBit(string name, ushort address, byte bitOffset) => Bit(name, FinsMemoryAreaCode.HoldingBit, address, bitOffset);

    /// <summary>声明 AR 字点。</summary>
    public FinsPointMap AuxiliaryWord(string name, ushort address) => Word(name, FinsMemoryAreaCode.AuxiliaryWord, address);

    /// <summary>声明 AR 位点。</summary>
    public FinsPointMap AuxiliaryBit(string name, ushort address, byte bitOffset) => Bit(name, FinsMemoryAreaCode.AuxiliaryBit, address, bitOffset);

    /// <summary>声明 TIM/CNT 当前值点。</summary>
    public FinsPointMap TimerCounterValue(string name, ushort address) => Word(name, FinsMemoryAreaCode.TimerCounterValue, address);

    /// <summary>声明 TIM/CNT 完成标志点。</summary>
    public FinsPointMap TimerCounterFlag(string name, ushort address) => Bit(name, FinsMemoryAreaCode.TimerCounterFlag, address);

    /// <summary>声明当前 EM Bank 字点。</summary>
    public FinsPointMap CurrentEmWord(string name, ushort address) => Word(name, FinsMemoryAreaCode.CurrentEmWord, address);

    /// <summary>声明当前 EM Bank 位点。</summary>
    public FinsPointMap CurrentEmBit(string name, ushort address, byte bitOffset) => Bit(name, FinsMemoryAreaCode.CurrentEmBit, address, bitOffset);

    /// <summary>声明指定 EM Bank 字点。</summary>
    public FinsPointMap EmWord(string name, int bank, ushort address) => Word(name, FinsMemoryAreaCode.EmBankWord(bank), address);

    /// <summary>声明指定 EM Bank 位点。</summary>
    public FinsPointMap EmBit(string name, int bank, ushort address, byte bitOffset) => Bit(name, FinsMemoryAreaCode.EmBankBit(bank), address, bitOffset);

    /// <summary>把已经声明的点标为可写，之后可通过 <see cref="IPointTable.WriteAsync"/> 按名称下发。</summary>
    public FinsPointMap Writable(string name)
    {
        var normalized = Normalize(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _points[i] = point.WithWritable(true);
                return this;
            }
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再标为可写。");
    }

    /// <summary>为已经声明的数值点设置或替换报警限。</summary>
    public FinsPointMap WithAlarmLimits(string name, double? low = null, double? high = null)
    {
        var normalized = Normalize(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (!string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (point.IsBit)
            {
                throw new ZeusException($"点 {normalized} 是 FINS 位点，不能配置数值报警限。");
            }

            _points[i] = point.WithAlarmLimits(new PointAlarmLimits(low, high));
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再配置报警限。");
    }

    private FinsPointMap AddWord(
        string name,
        FinsMemoryAreaCode area,
        ushort address,
        FinsDataType dataType,
        double? scale,
        PointAlarmLimits? alarmLimits)
    {
        var normalized = Normalize(name);
        EnsureWordArea(area, normalized);
        if (scale is { } factor && (factor <= 0 || !double.IsFinite(factor)))
        {
            throw new ZeusException($"点 {normalized} 的 scale 必须是大于 0 的有限数值。");
        }

        var kind = dataType switch
        {
            FinsDataType.Word when scale is null => PointValueKind.UInt16,
            _ when scale is not null => PointValueKind.Double,
            _ => PointValueKind.Object
        };
        Add(new FinsPointSpec(normalized, area, address, 0, dataType, kind, alarmLimits, scale: scale));
        return this;
    }

    private void Add(FinsPointSpec spec)
    {
        if (!_names.Add(spec.Name))
        {
            throw new ZeusException($"同一台 FINS 设备上点名 {spec.Name} 重复。");
        }

        _points.Add(spec);
    }

    private static void EnsureBitArea(FinsMemoryAreaCode area, string pointName)
    {
        if (!area.IsBit)
        {
            throw new ZeusException($"点 {pointName} 使用的 FINS 内存区 {area} 不是位区。");
        }
    }

    private static void EnsureWordArea(FinsMemoryAreaCode area, string pointName)
    {
        if (!area.IsWord)
        {
            throw new ZeusException($"点 {pointName} 使用的 FINS 内存区 {area} 不是字区。");
        }
    }

    private static void EnsureBitOffset(byte bitOffset, string pointName)
    {
        if (bitOffset > 15)
        {
            throw new ZeusException($"点 {pointName} 的 FINS 位偏移必须介于 0 与 15 之间。");
        }
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("FINS 点名不能为空。");
        }

        return name.Trim();
    }
}
