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
    internal ModbusPointSpec(
        string name,
        ModbusTable table,
        ushort address,
        PointValueKind kind,
        Func<ushort, object>? convert)
    {
        Name = name;
        Table = table;
        Address = address;
        Kind = kind;
        Convert = convert;
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
}
