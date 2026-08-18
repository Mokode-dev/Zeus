namespace Zeus;

/// <summary>
/// 声明一台 IEC104 站上要周期采集的信息对象点。
/// </summary>
public sealed class Iec104PointMap
{
    private readonly List<Iec104PointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<Iec104PointSpec> Points => _points;

    /// <summary>声明单点信息。</summary>
    public Iec104PointMap SinglePoint(string name, int address)
    {
        Add(new Iec104PointSpec(NormalizeName(name), NormalizeAddress(address), Iec104DataType.SinglePoint, null, PointValueKind.Boolean, null, false));
        return this;
    }

    /// <summary>声明归一化测量值，线值范围通常为 -1 到 1；配置 scale 后点表发布工程值。</summary>
    public Iec104PointMap Normalized(string name, int address, double? scale = null, PointAlarmLimits? alarmLimits = null)
    {
        ValidateNumericOptions(name, scale, alarmLimits);
        Add(new Iec104PointSpec(NormalizeName(name), NormalizeAddress(address), Iec104DataType.Normalized, scale, PointValueKind.Double, alarmLimits, false));
        return this;
    }

    /// <summary>声明标度化测量值，线值为有符号 16 位整数；配置 scale 后点表发布工程值。</summary>
    public Iec104PointMap Scaled(string name, int address, double? scale = null, PointAlarmLimits? alarmLimits = null)
    {
        ValidateNumericOptions(name, scale, alarmLimits);
        Add(new Iec104PointSpec(NormalizeName(name), NormalizeAddress(address), Iec104DataType.Scaled, scale, scale is null ? PointValueKind.Object : PointValueKind.Double, alarmLimits, false));
        return this;
    }

    /// <summary>声明短浮点测量值；配置 scale 后点表发布工程值。</summary>
    public Iec104PointMap ShortFloat(string name, int address, double? scale = null, PointAlarmLimits? alarmLimits = null)
    {
        ValidateNumericOptions(name, scale, alarmLimits);
        Add(new Iec104PointSpec(NormalizeName(name), NormalizeAddress(address), Iec104DataType.ShortFloat, scale, PointValueKind.Double, alarmLimits, false));
        return this;
    }

    /// <summary>把已经声明的点标为可写，之后可通过 <see cref="IPointTable.WriteAsync"/> 按名称下发。</summary>
    public Iec104PointMap Writable(string name)
    {
        var normalized = NormalizeName(name);
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
    public Iec104PointMap WithAlarmLimits(string name, double? low = null, double? high = null)
    {
        var normalized = NormalizeName(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (!string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (point.DataType == Iec104DataType.SinglePoint)
            {
                throw new ZeusException($"点 {normalized} 是 IEC104 单点信息，不能配置数值报警限。");
            }

            _points[i] = point.WithAlarmLimits(new PointAlarmLimits(low, high));
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再配置报警限。");
    }

    private void Add(Iec104PointSpec spec)
    {
        if (!_names.Add(spec.Name))
        {
            throw new ZeusException($"同一台 IEC104 设备上点名 {spec.Name} 重复。");
        }

        _points.Add(spec);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("IEC104 点名不能为空。");
        }

        return name.Trim();
    }

    private static int NormalizeAddress(int address)
    {
        Iec104Codec.ValidateInformationObjectAddress(address, nameof(address));
        return address;
    }

    private static void ValidateNumericOptions(string name, double? scale, PointAlarmLimits? alarmLimits)
    {
        if (scale is { } factor && (factor <= 0 || !double.IsFinite(factor)))
        {
            throw new ZeusException($"点 {name} 的 scale 必须是大于 0 的有限数值。");
        }

        if (alarmLimits?.Low > alarmLimits?.High)
        {
            throw new ZeusException($"点 {name} 的低报警限不能高于高报警限。");
        }
    }
}
