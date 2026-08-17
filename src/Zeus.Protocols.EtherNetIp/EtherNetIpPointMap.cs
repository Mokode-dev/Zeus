namespace Zeus;

/// <summary>
/// 声明一台 Allen-Bradley EtherNet/IP 设备上要周期采集的标签点。
/// </summary>
public sealed class EtherNetIpPointMap
{
    private readonly List<EtherNetIpPointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<EtherNetIpPointSpec> Points => _points;

    /// <summary>声明任意 CIP 原子标签。</summary>
    public EtherNetIpPointMap Tag(string name, string tagName, EtherNetIpDataType dataType, double? scale = null, PointAlarmLimits? alarmLimits = null)
    {
        var normalized = NormalizeName(name);
        var tag = NormalizeTag(tagName);
        if (scale is { } factor && (factor <= 0 || !double.IsFinite(factor)))
        {
            throw new ZeusException($"点 {normalized} 的 scale 必须是大于 0 的有限数值。");
        }

        if (scale is not null && dataType == EtherNetIpDataType.Bool)
        {
            throw new ZeusException($"点 {normalized} 是 EtherNet/IP BOOL，不能配置 scale。");
        }

        if (alarmLimits is not null && dataType == EtherNetIpDataType.Bool)
        {
            throw new ZeusException($"点 {normalized} 是 EtherNet/IP BOOL，不能配置报警限。");
        }

        Add(new EtherNetIpPointSpec(normalized, tag, dataType, DetermineKind(dataType, scale), alarmLimits, scale: scale));
        return this;
    }

    /// <summary>声明 BOOL 标签。</summary>
    public EtherNetIpPointMap Bool(string name, string tagName) => Tag(name, tagName, EtherNetIpDataType.Bool);

    /// <summary>声明 INT 标签。</summary>
    public EtherNetIpPointMap Int(string name, string tagName, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => Tag(name, tagName, EtherNetIpDataType.Int, scale, alarmLimits);

    /// <summary>声明 UINT 标签。</summary>
    public EtherNetIpPointMap UInt(string name, string tagName, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => Tag(name, tagName, EtherNetIpDataType.UInt, scale, alarmLimits);

    /// <summary>声明 DINT 标签。</summary>
    public EtherNetIpPointMap DInt(string name, string tagName, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => Tag(name, tagName, EtherNetIpDataType.DInt, scale, alarmLimits);

    /// <summary>声明 UDINT 标签。</summary>
    public EtherNetIpPointMap UDInt(string name, string tagName, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => Tag(name, tagName, EtherNetIpDataType.UDInt, scale, alarmLimits);

    /// <summary>声明 REAL 标签。</summary>
    public EtherNetIpPointMap Real(string name, string tagName, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => Tag(name, tagName, EtherNetIpDataType.Real, scale, alarmLimits);

    /// <summary>把已经声明的点标为可写，之后可通过 <see cref="IPointTable.WriteAsync"/> 按名称下发。</summary>
    public EtherNetIpPointMap Writable(string name)
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
    public EtherNetIpPointMap WithAlarmLimits(string name, double? low = null, double? high = null)
    {
        var normalized = NormalizeName(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (!string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (point.DataType == EtherNetIpDataType.Bool)
            {
                throw new ZeusException($"点 {normalized} 是 EtherNet/IP BOOL，不能配置数值报警限。");
            }

            _points[i] = point.WithAlarmLimits(new PointAlarmLimits(low, high));
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再配置报警限。");
    }

    private void Add(EtherNetIpPointSpec spec)
    {
        if (!_names.Add(spec.Name))
        {
            throw new ZeusException($"同一台 EtherNet/IP 设备上点名 {spec.Name} 重复。");
        }

        _points.Add(spec);
    }

    private static PointValueKind DetermineKind(EtherNetIpDataType dataType, double? scale)
    {
        if (dataType == EtherNetIpDataType.Bool)
        {
            return PointValueKind.Boolean;
        }

        if (scale is not null)
        {
            return PointValueKind.Double;
        }

        return dataType == EtherNetIpDataType.UInt ? PointValueKind.UInt16 : PointValueKind.Object;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("EtherNet/IP 点名不能为空。");
        }

        return name.Trim();
    }

    private static string NormalizeTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new ZeusException("EtherNet/IP 标签名不能为空。");
        }

        return tagName.Trim();
    }
}
