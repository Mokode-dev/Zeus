namespace Zeus;

/// <summary>
/// 单个 Omron FINS 点的采集描述。由 <see cref="FinsPointMap"/> 创建，业务代码通常不直接构造。
/// </summary>
public sealed class FinsPointSpec
{
    internal FinsPointSpec(
        string name,
        FinsMemoryAreaCode area,
        ushort address,
        byte bitOffset,
        FinsDataType dataType,
        PointValueKind kind,
        PointAlarmLimits? alarmLimits,
        bool writable = false,
        double? scale = null)
    {
        Name = name;
        Area = area;
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

    /// <summary>FINS 内存区代码。</summary>
    public FinsMemoryAreaCode Area { get; }

    /// <summary>字地址。</summary>
    public ushort Address { get; }

    /// <summary>位偏移，仅 <see cref="FinsDataType.Bit"/> 使用。</summary>
    public byte BitOffset { get; }

    /// <summary>数据类型。</summary>
    public FinsDataType DataType { get; }

    /// <summary>点表值类型。</summary>
    public PointValueKind Kind { get; }

    /// <summary>可选报警限。</summary>
    public PointAlarmLimits? AlarmLimits { get; }

    /// <summary>是否允许通过点表写回该地址。</summary>
    public bool Writable { get; }

    /// <summary>线性换算系数。采集时工程值 = 原始值 × Scale；写回时反向相除。</summary>
    public double? Scale { get; }

    /// <summary>该点是否为位点。</summary>
    public bool IsBit => DataType == FinsDataType.Bit;

    /// <summary>该点占用的字数量。</summary>
    public int WordCount => FinsCodec.GetWordCount(DataType);

    internal FinsPointSpec WithAlarmLimits(PointAlarmLimits alarmLimits)
        => new(Name, Area, Address, BitOffset, DataType, Kind, alarmLimits, Writable, Scale);

    internal FinsPointSpec WithWritable(bool writable)
        => new(Name, Area, Address, BitOffset, DataType, Kind, AlarmLimits, writable, Scale);
}
