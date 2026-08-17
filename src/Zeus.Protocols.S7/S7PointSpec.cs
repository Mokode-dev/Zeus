namespace Zeus;

/// <summary>
/// 单个 Siemens S7 点的采集描述。由 <see cref="S7PointMap"/> 创建，业务代码通常不直接构造。
/// </summary>
public sealed class S7PointSpec
{
    internal S7PointSpec(
        string name,
        S7Area area,
        S7DataType dataType,
        int byteOffset,
        int dbNumber,
        int bitOffset,
        PointValueKind kind,
        PointAlarmLimits? alarmLimits,
        bool writable = false,
        double? scale = null)
    {
        Name = name;
        Area = area;
        DataType = dataType;
        ByteOffset = byteOffset;
        DbNumber = dbNumber;
        BitOffset = bitOffset;
        Kind = kind;
        AlarmLimits = alarmLimits;
        Writable = writable;
        Scale = scale;
    }

    /// <summary>点名。</summary>
    public string Name { get; }

    /// <summary>S7 存储区。</summary>
    public S7Area Area { get; }

    /// <summary>DB 块号。非 DB 区为 0。</summary>
    public int DbNumber { get; }

    /// <summary>字节偏移。</summary>
    public int ByteOffset { get; }

    /// <summary>位偏移，仅 <see cref="S7DataType.Bool"/> 使用。</summary>
    public int BitOffset { get; }

    /// <summary>数据类型。</summary>
    public S7DataType DataType { get; }

    /// <summary>点表值类型。</summary>
    public PointValueKind Kind { get; }

    /// <summary>可选报警限。</summary>
    public PointAlarmLimits? AlarmLimits { get; }

    /// <summary>是否允许通过点表写回该地址。</summary>
    public bool Writable { get; }

    /// <summary>
    /// 线性换算系数。采集时 <c>工程值 = 原始值 * Scale</c>；写回时反向相除。
    /// </summary>
    public double? Scale { get; }

    /// <summary>该点占用的字节数。Bool 按 1 字节读写。</summary>
    public int ByteLength => S7Codec.GetByteLength(DataType);

    internal S7PointSpec WithAlarmLimits(PointAlarmLimits alarmLimits)
        => new(Name, Area, DataType, ByteOffset, DbNumber, BitOffset, Kind, alarmLimits, Writable, Scale);

    internal S7PointSpec WithWritable(bool writable)
        => new(Name, Area, DataType, ByteOffset, DbNumber, BitOffset, Kind, AlarmLimits, writable, Scale);
}
