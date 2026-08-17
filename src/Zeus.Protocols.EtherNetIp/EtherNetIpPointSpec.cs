namespace Zeus;

/// <summary>
/// 单个 EtherNet/IP 标签点的采集描述。由 <see cref="EtherNetIpPointMap"/> 创建。
/// </summary>
public sealed class EtherNetIpPointSpec
{
    internal EtherNetIpPointSpec(
        string name,
        string tagName,
        EtherNetIpDataType dataType,
        PointValueKind kind,
        PointAlarmLimits? alarmLimits,
        bool writable = false,
        double? scale = null)
    {
        Name = name;
        TagName = tagName;
        DataType = dataType;
        Kind = kind;
        AlarmLimits = alarmLimits;
        Writable = writable;
        Scale = scale;
    }

    /// <summary>点名。</summary>
    public string Name { get; }

    /// <summary>PLC 标签名。</summary>
    public string TagName { get; }

    /// <summary>CIP 数据类型。</summary>
    public EtherNetIpDataType DataType { get; }

    /// <summary>点表值类型。</summary>
    public PointValueKind Kind { get; }

    /// <summary>可选报警限。</summary>
    public PointAlarmLimits? AlarmLimits { get; }

    /// <summary>是否允许通过点表写回该标签。</summary>
    public bool Writable { get; }

    /// <summary>线性换算系数。采集时工程值 = 原始值 × Scale；写回时反向相除。</summary>
    public double? Scale { get; }

    internal EtherNetIpPointSpec WithAlarmLimits(PointAlarmLimits alarmLimits)
        => new(Name, TagName, DataType, Kind, alarmLimits, Writable, Scale);

    internal EtherNetIpPointSpec WithWritable(bool writable)
        => new(Name, TagName, DataType, Kind, AlarmLimits, writable, Scale);
}
