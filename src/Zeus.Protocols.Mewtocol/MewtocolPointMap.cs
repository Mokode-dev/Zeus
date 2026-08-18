namespace Zeus;

/// <summary>
/// 声明一台 Panasonic MEWTOCOL-COM 设备上要周期采集的点。连续同区字点会在采集时自动合并读取。
/// </summary>
public sealed class MewtocolPointMap
{
    private readonly List<MewtocolPointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<MewtocolPointSpec> Points => _points;

    /// <summary>声明数据寄存器位点。位点通过读写所在字实现。</summary>
    public MewtocolPointMap Bit(string name, MewtocolDataArea area, int address, byte bitOffset)
    {
        var normalized = Normalize(name);
        EnsureBitOffset(bitOffset, normalized);
        EnsureDataAddress(address, normalized);
        Add(new MewtocolPointSpec(normalized, area, null, address, bitOffset, MewtocolDataType.Bit, PointValueKind.Boolean, null));
        return this;
    }

    /// <summary>声明接点位点。位点通过读写所在接点字实现。</summary>
    public MewtocolPointMap Bit(string name, MewtocolContactArea area, int wordAddress, byte bitOffset)
    {
        var normalized = Normalize(name);
        EnsureBitOffset(bitOffset, normalized);
        EnsureContactAddress(wordAddress, normalized);
        Add(new MewtocolPointSpec(normalized, null, area, wordAddress, bitOffset, MewtocolDataType.Bit, PointValueKind.Boolean, null));
        return this;
    }

    /// <summary>声明数据寄存器字点，值为原始 <see cref="ushort"/>。</summary>
    public MewtocolPointMap Word(string name, MewtocolDataArea area, int address)
        => AddWord(name, area, null, address, MewtocolDataType.Word, null, null);

    /// <summary>声明带报警限的数据寄存器字点，值为原始 <see cref="ushort"/>。</summary>
    public MewtocolPointMap Word(string name, MewtocolDataArea area, int address, PointAlarmLimits alarmLimits)
        => AddWord(name, area, null, address, MewtocolDataType.Word, null, alarmLimits);

    /// <summary>声明带线性换算的数据寄存器字点。</summary>
    public MewtocolPointMap Word(string name, MewtocolDataArea area, int address, double scale)
        => AddWord(name, area, null, address, MewtocolDataType.Word, scale, null);

    /// <summary>声明带线性换算和报警限的数据寄存器字点。</summary>
    public MewtocolPointMap Word(string name, MewtocolDataArea area, int address, double scale, PointAlarmLimits alarmLimits)
        => AddWord(name, area, null, address, MewtocolDataType.Word, scale, alarmLimits);

    /// <summary>声明接点字点，值为原始 <see cref="ushort"/>。</summary>
    public MewtocolPointMap Word(string name, MewtocolContactArea area, int wordAddress)
        => AddWord(name, null, area, wordAddress, MewtocolDataType.Word, null, null);

    /// <summary>声明带报警限的接点字点，值为原始 <see cref="ushort"/>。</summary>
    public MewtocolPointMap Word(string name, MewtocolContactArea area, int wordAddress, PointAlarmLimits alarmLimits)
        => AddWord(name, null, area, wordAddress, MewtocolDataType.Word, null, alarmLimits);

    /// <summary>声明带线性换算的接点字点。</summary>
    public MewtocolPointMap Word(string name, MewtocolContactArea area, int wordAddress, double scale)
        => AddWord(name, null, area, wordAddress, MewtocolDataType.Word, scale, null);

    /// <summary>声明 16 位有符号整数数据寄存器点。</summary>
    public MewtocolPointMap Int16(string name, MewtocolDataArea area, int address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, null, address, MewtocolDataType.Int16, scale, alarmLimits);

    /// <summary>声明 16 位有符号整数接点字点。</summary>
    public MewtocolPointMap Int16(string name, MewtocolContactArea area, int wordAddress, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, null, area, wordAddress, MewtocolDataType.Int16, scale, alarmLimits);

    /// <summary>声明 32 位无符号整数数据寄存器点，占两个连续字。</summary>
    public MewtocolPointMap UInt32(string name, MewtocolDataArea area, int address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, null, address, MewtocolDataType.UInt32, scale, alarmLimits);

    /// <summary>声明 32 位无符号整数接点字点，占两个连续字。</summary>
    public MewtocolPointMap UInt32(string name, MewtocolContactArea area, int wordAddress, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, null, area, wordAddress, MewtocolDataType.UInt32, scale, alarmLimits);

    /// <summary>声明 32 位有符号整数数据寄存器点，占两个连续字。</summary>
    public MewtocolPointMap Int32(string name, MewtocolDataArea area, int address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, null, address, MewtocolDataType.Int32, scale, alarmLimits);

    /// <summary>声明 32 位有符号整数接点字点，占两个连续字。</summary>
    public MewtocolPointMap Int32(string name, MewtocolContactArea area, int wordAddress, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, null, area, wordAddress, MewtocolDataType.Int32, scale, alarmLimits);

    /// <summary>声明 32 位浮点数据寄存器点，占两个连续字。</summary>
    public MewtocolPointMap Real(string name, MewtocolDataArea area, int address, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, area, null, address, MewtocolDataType.Real, scale, alarmLimits);

    /// <summary>声明 32 位浮点接点字点，占两个连续字。</summary>
    public MewtocolPointMap Real(string name, MewtocolContactArea area, int wordAddress, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => AddWord(name, null, area, wordAddress, MewtocolDataType.Real, scale, alarmLimits);

    /// <summary>声明 DT 字点。</summary>
    public MewtocolPointMap DtWord(string name, int address) => Word(name, MewtocolDataArea.DataRegister, address);

    /// <summary>声明带线性换算的 DT 字点。</summary>
    public MewtocolPointMap DtWord(string name, int address, double scale) => Word(name, MewtocolDataArea.DataRegister, address, scale);

    /// <summary>声明 DT 位点。</summary>
    public MewtocolPointMap DtBit(string name, int address, byte bitOffset) => Bit(name, MewtocolDataArea.DataRegister, address, bitOffset);

    /// <summary>声明 LD 字点。</summary>
    public MewtocolPointMap LdWord(string name, int address) => Word(name, MewtocolDataArea.LinkDataRegister, address);

    /// <summary>声明 FL 字点。</summary>
    public MewtocolPointMap FlWord(string name, int address) => Word(name, MewtocolDataArea.FileRegister, address);

    /// <summary>声明 X 接点字点。</summary>
    public MewtocolPointMap XWord(string name, int wordAddress) => Word(name, MewtocolContactArea.ExternalInput, wordAddress);

    /// <summary>声明 X 接点位点。</summary>
    public MewtocolPointMap XBit(string name, int wordAddress, byte bitOffset) => Bit(name, MewtocolContactArea.ExternalInput, wordAddress, bitOffset);

    /// <summary>声明 Y 接点字点。</summary>
    public MewtocolPointMap YWord(string name, int wordAddress) => Word(name, MewtocolContactArea.ExternalOutput, wordAddress);

    /// <summary>声明 Y 接点位点。</summary>
    public MewtocolPointMap YBit(string name, int wordAddress, byte bitOffset) => Bit(name, MewtocolContactArea.ExternalOutput, wordAddress, bitOffset);

    /// <summary>声明 R 接点字点。</summary>
    public MewtocolPointMap RWord(string name, int wordAddress) => Word(name, MewtocolContactArea.InternalRelay, wordAddress);

    /// <summary>声明 R 接点位点。</summary>
    public MewtocolPointMap RBit(string name, int wordAddress, byte bitOffset) => Bit(name, MewtocolContactArea.InternalRelay, wordAddress, bitOffset);

    /// <summary>声明 L 接点字点。</summary>
    public MewtocolPointMap LWord(string name, int wordAddress) => Word(name, MewtocolContactArea.LinkRelay, wordAddress);

    /// <summary>声明 L 接点位点。</summary>
    public MewtocolPointMap LBit(string name, int wordAddress, byte bitOffset) => Bit(name, MewtocolContactArea.LinkRelay, wordAddress, bitOffset);

    /// <summary>把已经声明的点标为可写，之后可通过 <see cref="IPointTable.WriteAsync"/> 按名称下发。</summary>
    public MewtocolPointMap Writable(string name)
    {
        var normalized = Normalize(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                if (point.ContactArea == MewtocolContactArea.ExternalInput)
                {
                    throw new ZeusException($"点 {normalized} 位于 MEWTOCOL X 输入区，不能标为可写。");
                }

                _points[i] = point.WithWritable(true);
                return this;
            }
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再标为可写。");
    }

    /// <summary>为已经声明的数值点设置或替换报警限。</summary>
    public MewtocolPointMap WithAlarmLimits(string name, double? low = null, double? high = null)
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
                throw new ZeusException($"点 {normalized} 是 MEWTOCOL 位点，不能配置数值报警限。");
            }

            _points[i] = point.WithAlarmLimits(new PointAlarmLimits(low, high));
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再配置报警限。");
    }

    private MewtocolPointMap AddWord(
        string name,
        MewtocolDataArea? dataArea,
        MewtocolContactArea? contactArea,
        int address,
        MewtocolDataType dataType,
        double? scale,
        PointAlarmLimits? alarmLimits)
    {
        var normalized = Normalize(name);
        if (scale is { } factor && (factor <= 0 || !double.IsFinite(factor)))
        {
            throw new ZeusException($"点 {normalized} 的 scale 必须是大于 0 的有限数值。");
        }

        if (dataArea is not null)
        {
            EnsureDataAddress(address, normalized);
        }
        else
        {
            EnsureContactAddress(address, normalized);
        }

        var kind = dataType switch
        {
            MewtocolDataType.Word when scale is null => PointValueKind.UInt16,
            _ when scale is not null => PointValueKind.Double,
            _ => PointValueKind.Object
        };
        Add(new MewtocolPointSpec(normalized, dataArea, contactArea, address, 0, dataType, kind, alarmLimits, scale: scale));
        return this;
    }

    private void Add(MewtocolPointSpec spec)
    {
        if (!_names.Add(spec.Name))
        {
            throw new ZeusException($"同一台 MEWTOCOL 设备上点名 {spec.Name} 重复。");
        }

        _points.Add(spec);
    }

    private static void EnsureBitOffset(byte bitOffset, string pointName)
    {
        if (bitOffset > 15)
        {
            throw new ZeusException($"点 {pointName} 的 MEWTOCOL 位偏移必须介于 0 与 15 之间。");
        }
    }

    private static void EnsureDataAddress(int address, string pointName)
    {
        if (address is < 0 or > 99999)
        {
            throw new ZeusException($"点 {pointName} 的 MEWTOCOL 数据寄存器地址必须介于 0 与 99999 之间。");
        }
    }

    private static void EnsureContactAddress(int address, string pointName)
    {
        if (address is < 0 or > 9999)
        {
            throw new ZeusException($"点 {pointName} 的 MEWTOCOL 接点字地址必须介于 0 与 9999 之间。");
        }
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("MEWTOCOL 点名不能为空。");
        }

        return name.Trim();
    }
}
