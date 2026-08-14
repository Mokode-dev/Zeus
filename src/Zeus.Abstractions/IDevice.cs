namespace Zeus;

/// <summary>
/// 设备模型契约。设备组合通道与协议，向业务暴露有业务含义的属性与命令。
/// 周期采集仍由业务按需调用；登记请使用 <c>AddDevice</c> 或 <c>AddModbusRtu</c> / <c>AddModbusTcp</c>。
/// 0.2 起必须暴露所绑定的通道，以便运行中卸载通道时级联移除设备。
/// </summary>
public interface IDevice
{
    /// <summary>在宿主内唯一的设备名。</summary>
    string Name { get; }

    /// <summary>该设备使用的传输通道。卸载通道时据此级联移除设备。</summary>
    IChannel Channel { get; }
}
