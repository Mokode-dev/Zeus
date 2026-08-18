namespace Zeus;

/// <summary>
/// 声明一台 Omron Host Link 设备上要周期采集的点。连续同区字点会在采集时自动合并读取。
/// </summary>
public sealed class HostLinkPointMap
{
    private readonly List<HostLinkPointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<HostLinkPointSpec> Points => _points;

    /// <summary>声明通用位点。Host Link 位点通过读写所在字实现。</summary>
    public HostLinkPointMap Bit(string name, HostLinkArea area, ushort address, byte bitOffset)
    {
        var normalized = Normalize(name);
        EnsureBitOffset(bitOffset, normalized);
        Add(new HostLinkPointSpec(normalized, area, address, bitOffset, HostLinkDataType.Bit, PointValueKind.Boolean, null));
        return this;
    }

    /// <summary>声明通用字点，值为原始 <see cref="ushort"/>。</summary>
    public HostLinkPointMap Word(string name, HostLinkArea area, ushort address)
        => AddWord(name, area, address, HostLinkDataType.Word, null, null);

    /// <summary>声明带报警限的通用字点，值为原始 <see cref="ushort"/>。</summary>
    public HostLinkPointMap Word(string name, HostLinkArea area, ushort address, PointAlarmLimits alarmLimits)
        => AddWord(name, area, address, HostLinkDataType.Word, null, alarmLimits);

    /// <summary>声明带线性换算的通用字点。</summary>
    public HostLinkPointMap Word(string name, HostLinkArea area, ushort address, double scale)
        => AddWord(name, area, address, HostLinkDataType.Word, scale, null);

    /// <summary>声明带线性换算和报警限的通用字点。</summary>
    public HostLinkPointMap Word(string name, HostLinkArea area, ushort address, double scale, PointAlarmLimits alarmLimits)
        => AddWord(name, area, address, HostLinkDataType.Word, scale, alarmLimits);

    /// <summary>声明 16 位有符号整数点。</summary>
    public HostLinkPointMap Int16(string name, HostLinkArea area, ushort address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, address, HostLinkDataType.Int16, scale, alarmLimits);

    /// <summary>声明 32 位无符号整数点，占两个连续字。</summary>
    public HostLinkPointMap UInt32(string name, HostLinkArea area, ushort address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, address, HostLinkDataType.UInt32, scale, alarmLimits);

    /// <summary>声明 32 位有符号整数点，占两个连续字。</summary>
    public HostLinkPointMap Int32(string name, HostLinkArea area, ushort address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, address, HostLinkDataType.Int32, scale, alarmLimits);

    /// <summary>声明 32 位浮点点，占两个连续字。</summary>
    public HostLinkPointMap Real(string name, HostLinkArea area, ushort address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, address, HostLinkDataType.Real, scale, alarmLimits);

    /// <summary>声明 CIO 字点。</summary>
    public HostLinkPointMap CioWord(string name, ushort address) => Word(name, HostLinkArea.Cio, address);

    /// <summary>声明 CIO 位点。</summary>
    public HostLinkPointMap CioBit(string name, ushort address, byte bitOffset) => Bit(name, HostLinkArea.Cio, address, bitOffset);

    /// <summary>声明 LR 字点。</summary>
    public HostLinkPointMap LinkWord(string name, ushort address) => Word(name, HostLinkArea.Link, address);

    /// <summary>声明 LR 位点。</summary>
    public HostLinkPointMap LinkBit(string name, ushort address, byte bitOffset) => Bit(name, HostLinkArea.Link, address, bitOffset);

    /// <summary>声明 HR 字点。</summary>
    public HostLinkPointMap HoldingWord(string name, ushort address) => Word(name, HostLinkArea.Holding, address);

    /// <summary>声明 HR 位点。</summary>
    public HostLinkPointMap HoldingBit(string name, ushort address, byte bitOffset) => Bit(name, HostLinkArea.Holding, address, bitOffset);

    /// <summary>声明 AR 字点。</summary>
    public HostLinkPointMap AuxiliaryWord(string name, ushort address) => Word(name, HostLinkArea.Auxiliary, address);

    /// <summary>声明 AR 位点。</summary>
    public HostLinkPointMap AuxiliaryBit(string name, ushort address, byte bitOffset) => Bit(name, HostLinkArea.Auxiliary, address, bitOffset);

    /// <summary>声明 DM 字点。</summary>
    public HostLinkPointMap DmWord(string name, ushort address) => Word(name, HostLinkArea.DataMemory, address);

    /// <summary>声明带线性换算的 DM 字点。</summary>
    public HostLinkPointMap DmWord(string name, ushort address, double scale) => Word(name, HostLinkArea.DataMemory, address, scale);

    /// <summary>声明 DM 位点。</summary>
    public HostLinkPointMap DmBit(string name, ushort address, byte bitOffset) => Bit(name, HostLinkArea.DataMemory, address, bitOffset);

    /// <summary>把已经声明的点标为可写，之后可通过 <see cref="IPointTable.WriteAsync"/> 按名称下发。</summary>
    public HostLinkPointMap Writable(string name)
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
    public HostLinkPointMap WithAlarmLimits(string name, double? low = null, double? high = null)
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
                throw new ZeusException($"点 {normalized} 是 Host Link 位点，不能配置数值报警限。");
            }

            _points[i] = point.WithAlarmLimits(new PointAlarmLimits(low, high));
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再配置报警限。");
    }

    private HostLinkPointMap AddWord(
        string name,
        HostLinkArea area,
        ushort address,
        HostLinkDataType dataType,
        double? scale,
        PointAlarmLimits? alarmLimits)
    {
        var normalized = Normalize(name);
        if (scale is { } factor && (factor <= 0 || !double.IsFinite(factor)))
        {
            throw new ZeusException($"点 {normalized} 的 scale 必须是大于 0 的有限数值。");
        }

        var kind = dataType switch
        {
            HostLinkDataType.Word when scale is null => PointValueKind.UInt16,
            _ when scale is not null => PointValueKind.Double,
            _ => PointValueKind.Object
        };
        Add(new HostLinkPointSpec(normalized, area, address, 0, dataType, kind, alarmLimits, scale: scale));
        return this;
    }

    private void Add(HostLinkPointSpec spec)
    {
        if (!_names.Add(spec.Name))
        {
            throw new ZeusException($"同一台 Host Link 设备上点名 {spec.Name} 重复。");
        }

        _points.Add(spec);
    }

    private static void EnsureBitOffset(byte bitOffset, string pointName)
    {
        if (bitOffset > 15)
        {
            throw new ZeusException($"点 {pointName} 的 Host Link 位偏移必须介于 0 与 15 之间。");
        }
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("Host Link 点名不能为空。");
        }

        return name.Trim();
    }
}
