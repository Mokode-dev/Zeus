namespace Zeus;

/// <summary>
/// 单个 Mitsubishi MC 点的采集描述。由 <see cref="McPointMap"/> 创建，业务代码通常不直接构造。
/// </summary>
public sealed class McPointSpec
{
    /// <summary>
    /// 初始化点描述。
    /// </summary>
    /// <param name="name">设备内唯一的点名。</param>
    /// <param name="deviceCode">MC 软元件代码。</param>
    /// <param name="address">0 基软元件地址。</param>
    /// <param name="kind">点表中的值类型。</param>
    /// <param name="convert">字软元件换算；位软元件为 <c>null</c>。</param>
    /// <param name="alarmLimits">可选报警限。</param>
    /// <param name="writable">是否允许按点名写回。</param>
    /// <param name="scale">线性换算系数。写回时用工程值除以该系数得到寄存器值。</param>
    internal McPointSpec(
        string name,
        McDeviceCode deviceCode,
        int address,
        PointValueKind kind,
        Func<ushort, object>? convert,
        PointAlarmLimits? alarmLimits,
        bool writable = false,
        double? scale = null)
    {
        Name = name;
        DeviceCode = deviceCode;
        Address = address;
        Kind = kind;
        Convert = convert;
        AlarmLimits = alarmLimits;
        Writable = writable;
        Scale = scale;
    }

    /// <summary>点名。</summary>
    public string Name { get; }

    /// <summary>MC 软元件代码。</summary>
    public McDeviceCode DeviceCode { get; }

    /// <summary>0 基软元件地址。</summary>
    public int Address { get; }

    /// <summary>点表值类型。</summary>
    public PointValueKind Kind { get; }

    /// <summary>把原始字软元件转换为业务值。</summary>
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

    /// <summary>是否为位软元件点。</summary>
    public bool IsBit => IsBitDevice(DeviceCode);

    /// <summary>是否为字软元件点。</summary>
    public bool IsWord => IsWordDevice(DeviceCode);

    /// <summary>
    /// 返回带报警限的新点描述。
    /// </summary>
    /// <param name="alarmLimits">报警限。</param>
    internal McPointSpec WithAlarmLimits(PointAlarmLimits alarmLimits)
        => new(Name, DeviceCode, Address, Kind, Convert, alarmLimits, Writable, Scale);

    /// <summary>
    /// 返回改为可写或只读的新点描述。
    /// </summary>
    /// <param name="writable">是否可写。</param>
    internal McPointSpec WithWritable(bool writable)
        => new(Name, DeviceCode, Address, Kind, Convert, AlarmLimits, writable, Scale);

    internal static bool IsBitDevice(McDeviceCode deviceCode)
        => deviceCode is McDeviceCode.InternalRelay or McDeviceCode.InputRelay or McDeviceCode.OutputRelay;

    internal static bool IsWordDevice(McDeviceCode deviceCode)
        => deviceCode is McDeviceCode.DataRegister
            or McDeviceCode.LinkRegister
            or McDeviceCode.FileRegister
            or McDeviceCode.ExtendedFileRegister;
}
