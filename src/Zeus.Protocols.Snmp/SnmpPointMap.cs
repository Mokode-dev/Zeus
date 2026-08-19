namespace Zeus;

/// <summary>声明 SNMP OID 与 Zeus 点表之间的映射。</summary>
public sealed class SnmpPointMap
{
    private readonly List<SnmpPointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _oids = new(StringComparer.Ordinal);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<SnmpPointSpec> Points => _points;

    /// <summary>声明 Integer 点。</summary>
    public SnmpPointMap Integer(string name, string oid, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => Add(name, oid, SnmpDataType.Integer, scale is null ? PointValueKind.Object : PointValueKind.Double, scale, alarmLimits);

    /// <summary>声明 Gauge32 点。</summary>
    public SnmpPointMap Gauge32(string name, string oid, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => Add(name, oid, SnmpDataType.Gauge32, scale is null ? PointValueKind.Object : PointValueKind.Double, scale, alarmLimits);

    /// <summary>声明 Counter32 点。</summary>
    public SnmpPointMap Counter32(string name, string oid, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => Add(name, oid, SnmpDataType.Counter32, scale is null ? PointValueKind.Object : PointValueKind.Double, scale, alarmLimits);

    /// <summary>声明 TimeTicks 点。</summary>
    public SnmpPointMap TimeTicks(string name, string oid, double? scale = null, PointAlarmLimits? alarmLimits = null)
        => Add(name, oid, SnmpDataType.TimeTicks, scale is null ? PointValueKind.Object : PointValueKind.Double, scale, alarmLimits);

    /// <summary>声明 UTF-8 文本点。</summary>
    public SnmpPointMap Text(string name, string oid)
        => Add(name, oid, SnmpDataType.Text, PointValueKind.Object, null, null);

    /// <summary>声明字节串点。</summary>
    public SnmpPointMap OctetString(string name, string oid)
        => Add(name, oid, SnmpDataType.OctetString, PointValueKind.Object, null, null);

    /// <summary>声明 OID 值点。</summary>
    public SnmpPointMap ObjectIdentifier(string name, string oid)
        => Add(name, oid, SnmpDataType.ObjectIdentifier, PointValueKind.Object, null, null);

    /// <summary>声明 IPv4 地址点。</summary>
    public SnmpPointMap IpAddress(string name, string oid)
        => Add(name, oid, SnmpDataType.IpAddress, PointValueKind.Object, null, null);

    /// <summary>把已声明的点标为可写。</summary>
    public SnmpPointMap Writable(string name)
    {
        var index = FindIndex(name);
        _points[index] = _points[index] with { Writable = true };
        return this;
    }

    /// <summary>为已声明的数值点设置报警限。</summary>
    public SnmpPointMap WithAlarmLimits(string name, double? low = null, double? high = null)
    {
        if (low > high)
        {
            throw new ZeusException($"SNMP 点 {name} 的低报警限不能高于高报警限。");
        }

        var index = FindIndex(name);
        var point = _points[index];
        if (!SnmpCodec.IsNumeric(point.DataType))
        {
            throw new ZeusException($"SNMP 点 {point.Name} 不是数值点，不能配置报警限。");
        }

        _points[index] = point with { AlarmLimits = new PointAlarmLimits(low, high) };
        return this;
    }

    private SnmpPointMap Add(
        string name,
        string oid,
        SnmpDataType dataType,
        PointValueKind kind,
        double? scale,
        PointAlarmLimits? alarmLimits)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("SNMP 点名不能为空。");
        }

        if (scale is <= 0)
        {
            throw new ZeusException($"SNMP 点 {name} 的 scale 必须大于 0。");
        }

        if (alarmLimits?.Low > alarmLimits?.High)
        {
            throw new ZeusException($"SNMP 点 {name} 的低报警限不能高于高报警限。");
        }

        if (alarmLimits is not null && !SnmpCodec.IsNumeric(dataType))
        {
            throw new ZeusException($"SNMP 点 {name} 不是数值点，不能配置报警限。");
        }

        var normalizedName = name.Trim();
        var normalizedOid = SnmpCodec.NormalizeOid(oid);
        if (!_names.Add(normalizedName))
        {
            throw new ZeusException($"同一台 SNMP 设备上点名 {normalizedName} 重复。");
        }

        if (!_oids.Add(normalizedOid))
        {
            throw new ZeusException($"同一台 SNMP 设备上 OID {normalizedOid} 重复。");
        }

        _points.Add(new SnmpPointSpec(normalizedName, normalizedOid, dataType, kind, scale, alarmLimits, false));
        return this;
    }

    private int FindIndex(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("SNMP 点名不能为空。");
        }

        var normalized = name.Trim();
        for (var i = 0; i < _points.Count; i++)
        {
            if (string.Equals(_points[i].Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ZeusException($"找不到 SNMP 点 {normalized}，请先声明该点。");
    }
}
