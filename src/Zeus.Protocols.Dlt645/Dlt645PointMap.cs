namespace Zeus;

/// <summary>
/// 声明一台 DL/T 645 表计上要周期采集的数据项。
/// </summary>
public sealed class Dlt645PointMap
{
    private readonly List<Dlt645PointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<Dlt645PointSpec> Points => _points;

    /// <summary>声明低字节在前的 BCD 数值点。</summary>
    public Dlt645PointMap Bcd(
        string name,
        uint dataIdentifier,
        int dataLength = 4,
        double scale = 0.01,
        PointAlarmLimits? alarmLimits = null)
    {
        var normalized = Normalize(name);
        Dlt645Codec.EnsureDataLength(dataLength);
        Dlt645Codec.EnsureScale(scale);
        Add(new Dlt645PointSpec(normalized, dataIdentifier, Dlt645DataType.Bcd, dataLength, scale, PointValueKind.Double, alarmLimits));
        return this;
    }

    /// <summary>声明原始字节点。</summary>
    public Dlt645PointMap RawBytes(string name, uint dataIdentifier, int dataLength)
    {
        var normalized = Normalize(name);
        Dlt645Codec.EnsureDataLength(dataLength);
        Add(new Dlt645PointSpec(normalized, dataIdentifier, Dlt645DataType.RawBytes, dataLength, 1, PointValueKind.Object, null));
        return this;
    }

    /// <summary>声明组合有功总电能点，默认数据项 00000000，4 字节 BCD，0.01 kWh。</summary>
    public Dlt645PointMap TotalActiveEnergy(string name = "totalActiveEnergy")
        => Bcd(name, 0x00000000, dataLength: 4, scale: 0.01);

    /// <summary>声明 A 相电压点，默认数据项 02010100，2 字节 BCD，0.1 V。</summary>
    public Dlt645PointMap VoltageA(string name = "voltageA")
        => Bcd(name, 0x02010100, dataLength: 2, scale: 0.1);

    /// <summary>声明 A 相电流点，默认数据项 02020100，3 字节 BCD，0.001 A。</summary>
    public Dlt645PointMap CurrentA(string name = "currentA")
        => Bcd(name, 0x02020100, dataLength: 3, scale: 0.001);

    /// <summary>把已经声明的点标为可写，之后可通过 <see cref="IPointTable.WriteAsync"/> 按名称下发。</summary>
    public Dlt645PointMap Writable(string name)
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

    /// <summary>为已经声明的 BCD 数值点设置或替换报警限。</summary>
    public Dlt645PointMap WithAlarmLimits(string name, double? low = null, double? high = null)
    {
        var normalized = Normalize(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (!string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (point.DataType != Dlt645DataType.Bcd)
            {
                throw new ZeusException($"点 {normalized} 是 DL/T 645 原始字节点，不能配置数值报警限。");
            }

            _points[i] = point.WithAlarmLimits(new PointAlarmLimits(low, high));
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再配置报警限。");
    }

    private void Add(Dlt645PointSpec spec)
    {
        if (!_names.Add(spec.Name))
        {
            throw new ZeusException($"同一台 DL/T 645 设备上点名 {spec.Name} 重复。");
        }

        _points.Add(spec);
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("DL/T 645 点名不能为空。");
        }

        return name.Trim();
    }
}
