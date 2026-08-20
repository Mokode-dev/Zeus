using Microsoft.Extensions.Logging;

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
        : this(name, channel, null)
    {
    }

    /// <summary>
    /// 初始化设备并注入诊断日志。
    /// </summary>
    /// <param name="name">宿主内唯一名称。</param>
    /// <param name="channel">该设备使用的传输通道。</param>
    /// <param name="logger">诊断日志。允许为 <c>null</c>，此时使用空记录器。</param>
    protected DeviceBase(string name, IChannel channel, ILogger? logger)
    {
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name));
        Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        Logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IChannel Channel { get; }

    /// <summary>设备诊断日志。未注入时为空记录器，调用安全。</summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// 打开带设备名与通道名的日志作用域，便于把一次采集或写回串到同一条链路。
    /// </summary>
    protected IDisposable BeginDeviceScope()
        => LogScope.Begin(Logger, new Dictionary<string, object>
        {
            ["Device"] = Name,
            ["Channel"] = Channel.Name
        });

    /// <summary>
    /// 记录本轮采集失败。点名可选；分组读取失败时只记设备级。
    /// </summary>
    /// <param name="exception">失败原因。</param>
    /// <param name="pointName">设备内短名。为 <c>null</c> 时只带设备作用域。</param>
    protected void LogAcquisitionFailed(Exception exception, string? pointName = null)
    {
        using var scope = BeginDeviceScope();
        if (string.IsNullOrWhiteSpace(pointName))
        {
            Logger.LogWarning(ZeusLogEvents.AcquisitionFailed, exception, "设备 {Device} 本轮采集失败。", Name);
            return;
        }

        Logger.LogWarning(
            ZeusLogEvents.AcquisitionFailed,
            exception,
            "设备 {Device} 点 {Point} 本轮采集失败。",
            Name,
            pointName);
    }

    /// <summary>
    /// 记录按点名写回失败。调用方仍应更新点表错误并抛出。
    /// </summary>
    /// <param name="exception">失败原因。</param>
    /// <param name="pointName">设备内短名。</param>
    protected void LogWriteFailed(Exception exception, string pointName)
    {
        using var scope = BeginDeviceScope();
        Logger.LogWarning(
            ZeusLogEvents.PointWriteFailed,
            exception,
            "设备 {Device} 写回点 {Point} 失败。",
            Name,
            pointName);
    }
}
