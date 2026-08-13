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
    }

    /// <summary>点名，不含设备前缀。</summary>
    public string Name { get; }

    /// <summary>所属设备。</summary>
    public string DeviceName { get; }

    /// <summary>值类型。</summary>
    public PointValueKind Kind { get; }

    /// <summary>限定名，格式为 <c>设备.点</c>，在整个宿主内唯一。</summary>
    public string QualifiedName => DeviceName + "." + Name;
}
