namespace Zeus;

/// <summary>
/// 声明一台 Siemens S7 设备上要周期采集的点。
/// DB、M 与 Q 区可再调用 <see cref="Writable"/>，以便按点名写回。
/// </summary>
public sealed class S7PointMap
{
    private const int MaxByteOffset = 0x1FFFFF;

    private readonly List<S7PointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<S7PointSpec> Points => _points;

    /// <summary>声明一个通用 S7 点。</summary>
    public S7PointMap Point(
        string name,
        S7Area area,
        S7DataType dataType,
        int byteOffset,
        int dbNumber = 0,
        int bitOffset = 0,
        PointAlarmLimits? alarmLimits = null)
        => AddPoint(name, area, dataType, byteOffset, dbNumber, bitOffset, null, alarmLimits);

    /// <summary>声明一个带线性换算的 S7 数值点。Bool 点不能使用换算。</summary>
    public S7PointMap ScaledPoint(
        string name,
        S7Area area,
        S7DataType dataType,
        int byteOffset,
        double scale,
        int dbNumber = 0,
        int bitOffset = 0,
        PointAlarmLimits? alarmLimits = null)
        => AddPoint(name, area, dataType, byteOffset, dbNumber, bitOffset, scale, alarmLimits);

    /// <summary>声明 DBX 位点。</summary>
    public S7PointMap DbBool(string name, int dbNumber, int byteOffset, int bitOffset)
        => Point(name, S7Area.DataBlock, S7DataType.Bool, byteOffset, dbNumber, bitOffset);

    /// <summary>声明 DBB 字节点。</summary>
    public S7PointMap DbByte(string name, int dbNumber, int byteOffset)
        => Point(name, S7Area.DataBlock, S7DataType.Byte, byteOffset, dbNumber);

    /// <summary>声明 DBW 字点。</summary>
    public S7PointMap DbWord(string name, int dbNumber, int byteOffset)
        => Point(name, S7Area.DataBlock, S7DataType.Word, byteOffset, dbNumber);

    /// <summary>声明 DBW 字点并应用线性换算。</summary>
    public S7PointMap DbWord(string name, int dbNumber, int byteOffset, double scale)
        => ScaledPoint(name, S7Area.DataBlock, S7DataType.Word, byteOffset, scale, dbNumber);

    /// <summary>声明 DBD 双字点。</summary>
    public S7PointMap DbDWord(string name, int dbNumber, int byteOffset)
        => Point(name, S7Area.DataBlock, S7DataType.DWord, byteOffset, dbNumber);

    /// <summary>声明 DBW 有符号整数点。</summary>
    public S7PointMap DbInt(string name, int dbNumber, int byteOffset)
        => Point(name, S7Area.DataBlock, S7DataType.Int, byteOffset, dbNumber);

    /// <summary>声明 DBW 有符号整数点并应用线性换算。</summary>
    public S7PointMap DbInt(string name, int dbNumber, int byteOffset, double scale)
        => ScaledPoint(name, S7Area.DataBlock, S7DataType.Int, byteOffset, scale, dbNumber);

    /// <summary>声明 DBD 有符号整数点。</summary>
    public S7PointMap DbDInt(string name, int dbNumber, int byteOffset)
        => Point(name, S7Area.DataBlock, S7DataType.DInt, byteOffset, dbNumber);

    /// <summary>声明 DBD 有符号整数点并应用线性换算。</summary>
    public S7PointMap DbDInt(string name, int dbNumber, int byteOffset, double scale)
        => ScaledPoint(name, S7Area.DataBlock, S7DataType.DInt, byteOffset, scale, dbNumber);

    /// <summary>声明 DBD 浮点点。</summary>
    public S7PointMap DbReal(string name, int dbNumber, int byteOffset)
        => Point(name, S7Area.DataBlock, S7DataType.Real, byteOffset, dbNumber);

    /// <summary>声明 DBD 浮点点并应用线性换算。</summary>
    public S7PointMap DbReal(string name, int dbNumber, int byteOffset, double scale)
        => ScaledPoint(name, S7Area.DataBlock, S7DataType.Real, byteOffset, scale, dbNumber);

    /// <summary>声明 M 位点。</summary>
    public S7PointMap MarkerBool(string name, int byteOffset, int bitOffset)
        => Point(name, S7Area.Merkers, S7DataType.Bool, byteOffset, 0, bitOffset);

    /// <summary>声明 MB 字节点。</summary>
    public S7PointMap MarkerByte(string name, int byteOffset)
        => Point(name, S7Area.Merkers, S7DataType.Byte, byteOffset);

    /// <summary>声明 MW 字点。</summary>
    public S7PointMap MarkerWord(string name, int byteOffset)
        => Point(name, S7Area.Merkers, S7DataType.Word, byteOffset);

    /// <summary>声明 MD 双字点。</summary>
    public S7PointMap MarkerDWord(string name, int byteOffset)
        => Point(name, S7Area.Merkers, S7DataType.DWord, byteOffset);

    /// <summary>声明 MW 有符号整数点。</summary>
    public S7PointMap MarkerInt(string name, int byteOffset)
        => Point(name, S7Area.Merkers, S7DataType.Int, byteOffset);

    /// <summary>声明 MD 有符号整数点。</summary>
    public S7PointMap MarkerDInt(string name, int byteOffset)
        => Point(name, S7Area.Merkers, S7DataType.DInt, byteOffset);

    /// <summary>声明 MD 浮点点。</summary>
    public S7PointMap MarkerReal(string name, int byteOffset)
        => Point(name, S7Area.Merkers, S7DataType.Real, byteOffset);

    /// <summary>声明 I 位点。</summary>
    public S7PointMap InputBool(string name, int byteOffset, int bitOffset)
        => Point(name, S7Area.Inputs, S7DataType.Bool, byteOffset, 0, bitOffset);

    /// <summary>声明 IB 字节点。</summary>
    public S7PointMap InputByte(string name, int byteOffset)
        => Point(name, S7Area.Inputs, S7DataType.Byte, byteOffset);

    /// <summary>声明 IW 字点。</summary>
    public S7PointMap InputWord(string name, int byteOffset)
        => Point(name, S7Area.Inputs, S7DataType.Word, byteOffset);

    /// <summary>声明 ID 双字点。</summary>
    public S7PointMap InputDWord(string name, int byteOffset)
        => Point(name, S7Area.Inputs, S7DataType.DWord, byteOffset);

    /// <summary>声明 IW 有符号整数点。</summary>
    public S7PointMap InputInt(string name, int byteOffset)
        => Point(name, S7Area.Inputs, S7DataType.Int, byteOffset);

    /// <summary>声明 ID 有符号整数点。</summary>
    public S7PointMap InputDInt(string name, int byteOffset)
        => Point(name, S7Area.Inputs, S7DataType.DInt, byteOffset);

    /// <summary>声明 ID 浮点点。</summary>
    public S7PointMap InputReal(string name, int byteOffset)
        => Point(name, S7Area.Inputs, S7DataType.Real, byteOffset);

    /// <summary>声明 Q 位点。</summary>
    public S7PointMap OutputBool(string name, int byteOffset, int bitOffset)
        => Point(name, S7Area.Outputs, S7DataType.Bool, byteOffset, 0, bitOffset);

    /// <summary>声明 QB 字节点。</summary>
    public S7PointMap OutputByte(string name, int byteOffset)
        => Point(name, S7Area.Outputs, S7DataType.Byte, byteOffset);

    /// <summary>声明 QW 字点。</summary>
    public S7PointMap OutputWord(string name, int byteOffset)
        => Point(name, S7Area.Outputs, S7DataType.Word, byteOffset);

    /// <summary>声明 QD 双字点。</summary>
    public S7PointMap OutputDWord(string name, int byteOffset)
        => Point(name, S7Area.Outputs, S7DataType.DWord, byteOffset);

    /// <summary>声明 QW 有符号整数点。</summary>
    public S7PointMap OutputInt(string name, int byteOffset)
        => Point(name, S7Area.Outputs, S7DataType.Int, byteOffset);

    /// <summary>声明 QD 有符号整数点。</summary>
    public S7PointMap OutputDInt(string name, int byteOffset)
        => Point(name, S7Area.Outputs, S7DataType.DInt, byteOffset);

    /// <summary>声明 QD 浮点点。</summary>
    public S7PointMap OutputReal(string name, int byteOffset)
        => Point(name, S7Area.Outputs, S7DataType.Real, byteOffset);

    /// <summary>
    /// 把已经声明的点标为可写，之后可通过 <see cref="IPointTable.WriteAsync"/> 按名称下发。
    /// 输入区 I 只读，不能标为可写。
    /// </summary>
    public S7PointMap Writable(string name)
    {
        var normalized = Normalize(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (!string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (point.Area == S7Area.Inputs)
            {
                throw new ZeusException($"点 {normalized} 位于 S7 输入区 I，该数据区只读，不能标为可写。");
            }

            _points[i] = point.WithWritable(true);
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再标为可写。");
    }

    /// <summary>为已经声明的数值点设置或替换报警限。</summary>
    public S7PointMap WithAlarmLimits(string name, double? low = null, double? high = null)
    {
        var normalized = Normalize(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (!string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (point.DataType == S7DataType.Bool)
            {
                throw new ZeusException($"点 {normalized} 是布尔点，不能配置数值报警限。");
            }

            _points[i] = point.WithAlarmLimits(new PointAlarmLimits(low, high));
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再配置报警限。");
    }

    private S7PointMap AddPoint(
        string name,
        S7Area area,
        S7DataType dataType,
        int byteOffset,
        int dbNumber,
        int bitOffset,
        double? scale,
        PointAlarmLimits? alarmLimits)
    {
        var normalized = Normalize(name);
        ValidateAddress(normalized, area, dataType, byteOffset, dbNumber, bitOffset);
        if (scale is not null && dataType == S7DataType.Bool)
        {
            throw new ZeusException($"点 {normalized} 是布尔点，不能配置 scale。");
        }

        if (scale is { } value && (value <= 0 || !double.IsFinite(value)))
        {
            throw new ZeusException($"点 {normalized} 的 scale 必须是大于 0 的有限数值。");
        }

        var kind = dataType switch
        {
            S7DataType.Bool => PointValueKind.Boolean,
            S7DataType.Word when scale is null => PointValueKind.UInt16,
            _ when scale is not null => PointValueKind.Double,
            _ => PointValueKind.Object
        };

        Add(new S7PointSpec(normalized, area, dataType, byteOffset, dbNumber, bitOffset, kind, alarmLimits, scale: scale));
        return this;
    }

    private void Add(S7PointSpec spec)
    {
        if (!_names.Add(spec.Name))
        {
            throw new ZeusException($"同一台 S7 设备上点名 {spec.Name} 重复。");
        }

        _points.Add(spec);
    }

    private static void ValidateAddress(string pointName, S7Area area, S7DataType dataType, int byteOffset, int dbNumber, int bitOffset)
    {
        if (byteOffset is < 0 or > MaxByteOffset)
        {
            throw new ZeusException($"点 {pointName} 的 S7 字节地址必须介于 0 与 {MaxByteOffset} 之间，当前为 {byteOffset}。");
        }

        if (area == S7Area.DataBlock)
        {
            if (dbNumber is <= 0 or > ushort.MaxValue)
            {
                throw new ZeusException($"点 {pointName} 位于 DB 区时 dbNumber 必须介于 1 与 65535 之间。");
            }
        }
        else if (dbNumber != 0)
        {
            throw new ZeusException($"点 {pointName} 不在 DB 区，dbNumber 必须为 0。");
        }

        if (dataType == S7DataType.Bool)
        {
            if (bitOffset is < 0 or > 7)
            {
                throw new ZeusException($"点 {pointName} 的 bitOffset 必须介于 0 与 7 之间。");
            }
        }
        else if (bitOffset != 0)
        {
            throw new ZeusException($"点 {pointName} 不是 Bool 类型，bitOffset 必须为 0。");
        }
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("S7 点名不能为空。");
        }

        return name.Trim();
    }
}
