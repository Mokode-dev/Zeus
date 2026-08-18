namespace Zeus;

/// <summary>
/// 单个 Panasonic MEWTOCOL-COM 点的采集描述。由 <see cref="MewtocolPointMap"/> 创建，业务代码通常不直接构造。
/// </summary>
public sealed class MewtocolPointSpec
{
    internal MewtocolPointSpec(
        string name,
        MewtocolDataArea? dataArea,
        MewtocolContactArea? contactArea,
        int address,
        byte bitOffset,
        MewtocolDataType dataType,
        PointValueKind kind,
        PointAlarmLimits? alarmLimits,
        bool writable = false,
        double? scale = null)
    {
        Name = name;
        DataArea = dataArea;
        ContactArea = contactArea;
        Address = address;
        BitOffset = bitOffset;
        DataType = dataType;
        Kind = kind;
        AlarmLimits = alarmLimits;
        Writable = writable;
        Scale = scale;
    }

    /// <summary>点名。</summary>
    public string Name { get; }

    /// <summary>DT / LD / FL 数据寄存器区。接点区点为 <c>null</c>。</summary>
    public MewtocolDataArea? DataArea { get; }

    /// <summary>X / Y / R / L 接点区。数据寄存器点为 <c>null</c>。</summary>
    public MewtocolContactArea? ContactArea { get; }

    /// <summary>字地址或接点字地址。</summary>
    public int Address { get; }

    /// <summary>位偏移，仅 <see cref="MewtocolDataType.Bit"/> 使用。</summary>
    public byte BitOffset { get; }

    /// <summary>数据类型。</summary>
    public MewtocolDataType DataType { get; }

    /// <summary>点表值类型。</summary>
    public PointValueKind Kind { get; }

    /// <summary>可选报警限。</summary>
    public PointAlarmLimits? AlarmLimits { get; }

    /// <summary>是否允许通过点表写回该地址。</summary>
    public bool Writable { get; }

    /// <summary>线性换算系数。采集时工程值 = 原始值 × Scale；写回时反向相除。</summary>
    public double? Scale { get; }

    /// <summary>该点是否为位点。</summary>
    public bool IsBit => DataType == MewtocolDataType.Bit;

    /// <summary>该点是否位于接点区。</summary>
    public bool IsContact => ContactArea is not null;

    /// <summary>该点占用或读取的字数量。</summary>
    public int WordCount => MewtocolCodec.GetWordCount(DataType);

    internal MewtocolPointSpec WithAlarmLimits(PointAlarmLimits alarmLimits)
        => new(Name, DataArea, ContactArea, Address, BitOffset, DataType, Kind, alarmLimits, Writable, Scale);

    internal MewtocolPointSpec WithWritable(bool writable)
        => new(Name, DataArea, ContactArea, Address, BitOffset, DataType, Kind, AlarmLimits, writable, Scale);
}
