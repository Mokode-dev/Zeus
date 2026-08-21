namespace Zeus;

/// <summary>
/// 单个 Modbus 点的采集描述。由 <see cref="ModbusPointMap"/> 创建，业务代码不必直接构造。
/// </summary>
public sealed class ModbusPointSpec
{
    /// <summary>
    /// 初始化点描述。
    /// </summary>
    /// <param name="name">设备内唯一的点名。</param>
    /// <param name="table">数据区。</param>
    /// <param name="address">起始地址（0 基）。</param>
    /// <param name="kind">点表中的值类型。</param>
    /// <param name="convert">寄存器换算；线圈点为 <c>null</c>。</param>
    /// <param name="alarmLimits">可选报警限。</param>
    /// <param name="writable">是否允许按点名写回。</param>
    /// <param name="scale">线性换算系数。写回时用工程值除以该系数得到寄存器值。</param>
    /// <param name="signed">为 <c>true</c> 时把寄存器按有符号 Int16 解释，再乘以 scale。</param>
    internal ModbusPointSpec(
        string name,
        ModbusTable table,
        ushort address,
        PointValueKind kind,
        Func<ushort, object>? convert,
        PointAlarmLimits? alarmLimits,
        bool writable = false,
        double? scale = null,
        bool signed = false)
    {
        Name = name;
        Table = table;
        Address = address;
        Kind = kind;
        Convert = convert;
        AlarmLimits = alarmLimits;
        Writable = writable;
        Scale = scale;
        Signed = signed;
    }

    /// <summary>点名。</summary>
    public string Name { get; }

    /// <summary>数据区。</summary>
    public ModbusTable Table { get; }

    /// <summary>0 基地址。</summary>
    public ushort Address { get; }

    /// <summary>点表值类型。</summary>
    public PointValueKind Kind { get; }

    /// <summary>把原始寄存器转换为业务值。</summary>
    public Func<ushort, object>? Convert { get; }

    /// <summary>可选报警限。</summary>
    public PointAlarmLimits? AlarmLimits { get; }

    /// <summary>是否允许通过点表写回该地址。</summary>
    public bool Writable { get; }

    /// <summary>
    /// 线性换算系数。采集时 <c>工程值 = 原始值 * Scale</c>；写回时反向相除。
    /// 仅使用自定义 <see cref="Convert"/>、未提供系数时为空，此时无法自动反算。
    /// </summary>
    public double? Scale { get; }

    /// <summary>
    /// 是否按有符号 Int16 解释原始寄存器。写回时同样限制在 <see cref="short"/> 范围。
    /// </summary>
    public bool Signed { get; }

    /// <summary>
    /// 返回带报警限的新点描述。
    /// </summary>
    /// <param name="alarmLimits">报警限。</param>
    internal ModbusPointSpec WithAlarmLimits(PointAlarmLimits alarmLimits)
        => new(Name, Table, Address, Kind, Convert, alarmLimits, Writable, Scale, Signed);

    /// <summary>
    /// 返回改为可写或只读的新点描述。
    /// </summary>
    /// <param name="writable">是否可写。</param>
    internal ModbusPointSpec WithWritable(bool writable)
        => new(Name, Table, Address, Kind, Convert, AlarmLimits, writable, Scale, Signed);
}
