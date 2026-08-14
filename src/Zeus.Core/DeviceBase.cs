namespace Zeus;

/// <summary>
/// 设备模型基类。固化命名与通道关联；具体协议由派生类或 <c>ModbusDevice</c> 实现。
/// </summary>
public abstract class DeviceBase : IDevice
{
    /// <summary>
    /// 初始化设备。
    /// </summary>
    /// <param name="name">宿主内唯一名称。</param>
    /// <param name="channel">该设备使用的传输通道。</param>
    protected DeviceBase(string name, IChannel channel)
    {
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name));
        Channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IChannel Channel { get; }
}
