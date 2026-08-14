namespace Zeus;

/// <summary>
/// 点表中一个点的静态定义。运行期快照见 <see cref="PointSnapshot"/>。
/// </summary>
public sealed class PointDefinition
{
    /// <summary>
    /// 创建点定义。
    /// </summary>
    /// <param name="name">点名，在设备内唯一。</param>
    /// <param name="deviceName">所属设备名。</param>
    /// <param name="kind">值类型。</param>
    public PointDefinition(string name, string deviceName, PointValueKind kind)
        : this(name, deviceName, kind, null)
    {
    }

    /// <summary>
    /// 创建带报警限的点定义。默认只读，仅由采集循环更新。
    /// </summary>
    /// <param name="name">点名，在设备内唯一。</param>
    /// <param name="deviceName">所属设备名。</param>
    /// <param name="kind">值类型。</param>
    /// <param name="alarmLimits">报警限。</param>
    public PointDefinition(string name, string deviceName, PointValueKind kind, PointAlarmLimits? alarmLimits)
        : this(name, deviceName, kind, alarmLimits, writable: false)
    {
    }

    /// <summary>
    /// 创建点定义，并可声明该点允许按名称写回设备。
    /// </summary>
    /// <param name="name">点名，在设备内唯一。</param>
    /// <param name="deviceName">所属设备名。</param>
    /// <param name="kind">值类型。</param>
    /// <param name="alarmLimits">报警限。</param>
    /// <param name="writable">为 <c>true</c> 时允许 <see cref="IPointTable.WriteAsync"/> 下发。</param>
    public PointDefinition(
        string name,
        string deviceName,
        PointValueKind kind,
        PointAlarmLimits? alarmLimits,
        bool writable)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("点名不能为空。请使用有业务含义的名称，例如 temperature。");
        }

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new ZeusException("点所属的设备名不能为空。");
        }

        Name = name.Trim();
        DeviceName = deviceName.Trim();
        Kind = kind;
        AlarmLimits = alarmLimits;
        Writable = writable;
    }

    /// <summary>点名，不含设备前缀。</summary>
    public string Name { get; }

    /// <summary>所属设备。</summary>
    public string DeviceName { get; }

    /// <summary>值类型。</summary>
    public PointValueKind Kind { get; }

    /// <summary>可选报警限。仅数值点会得到确定的报警状态。</summary>
    public PointAlarmLimits? AlarmLimits { get; }

    /// <summary>
    /// 是否允许通过点表写回设备。
    /// 只读测量点应为 <c>false</c>；设定值、开关等操作点设为 <c>true</c>。
    /// </summary>
    public bool Writable { get; }

    /// <summary>限定名，格式为 <c>设备.点</c>，在整个宿主内唯一。</summary>
    public string QualifiedName => DeviceName + "." + Name;
}
