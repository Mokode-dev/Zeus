namespace Zeus;

/// <summary>
/// 单个 DL/T 645 数据项的采集描述。由 <see cref="Dlt645PointMap"/> 创建，业务代码通常不直接构造。
/// </summary>
public sealed class Dlt645PointSpec
{
    internal Dlt645PointSpec(
        string name,
        uint dataIdentifier,
        Dlt645DataType dataType,
        int dataLength,
        double scale,
        PointValueKind kind,
        PointAlarmLimits? alarmLimits,
        bool writable = false)
    {
        Name = name;
        DataIdentifier = dataIdentifier;
        DataType = dataType;
        DataLength = dataLength;
        Scale = scale;
        Kind = kind;
        AlarmLimits = alarmLimits;
        Writable = writable;
    }

    /// <summary>点名。</summary>
    public string Name { get; }

    /// <summary>DL/T 645 四字节数据项标识，按常见手册写法使用十六进制表示。</summary>
    public uint DataIdentifier { get; }

    /// <summary>解码方式。</summary>
    public Dlt645DataType DataType { get; }

    /// <summary>数据项有效载荷长度，不含四字节数据项标识。</summary>
    public int DataLength { get; }

    /// <summary>BCD 换算系数。采集时工程值 = BCD 原始整数 × Scale；写回时反向相除。</summary>
    public double Scale { get; }

    /// <summary>点表值类型。</summary>
    public PointValueKind Kind { get; }

    /// <summary>可选报警限。</summary>
    public PointAlarmLimits? AlarmLimits { get; }

    /// <summary>是否允许通过点表写回该数据项。</summary>
    public bool Writable { get; }

    internal Dlt645PointSpec WithAlarmLimits(PointAlarmLimits alarmLimits)
        => new(Name, DataIdentifier, DataType, DataLength, Scale, Kind, alarmLimits, Writable);

    internal Dlt645PointSpec WithWritable(bool writable)
        => new(Name, DataIdentifier, DataType, DataLength, Scale, Kind, AlarmLimits, writable);
}
