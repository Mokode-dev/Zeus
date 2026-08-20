namespace Zeus;

/// <summary>
/// Zeus 应用宿主。负责组合根、生命周期以及通道/设备目录。
/// 界面框架不应出现在本接口上：WinForms 与 WPF 只是宿主的消费者。
/// </summary>
public interface IZeusHost : IAsyncDisposable
{
    /// <summary>已注册通道目录。</summary>
    IChannelRegistry Channels { get; }

    /// <summary>已注册设备目录。</summary>
    IDeviceRegistry Devices { get; }

    /// <summary>周期采集点表。未声明任何点时集合为空。</summary>
    IPointTable Points { get; }

    /// <summary>点报警队列。采集越限时产生记录，可确认、复归。</summary>
    IPointAlarmTable Alarms { get; }

    /// <summary>底层服务提供者，高级场景可由此解析自定义服务。</summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// 宿主是否处于已启动状态。为 <c>true</c> 时通道应保持打开，采集与自动重连会运行。
    /// 停止后再 <see cref="StartAsync"/> 会重新打开通道并恢复采集。
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 启动宿主：按注册顺序打开通道并启动后台服务。
    /// 对已启动的宿主重复调用是幂等的。停止后再调用会重新打开通道。
    /// </summary>
    /// <param name="cancellationToken">取消启动。</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止宿主：暂停采集与自动重连，并按相反顺序关闭通道。
    /// 底层日志与配置监视仍保持，以便再次 <see cref="StartAsync"/>。
    /// 释放资源请调用 <see cref="IAsyncDisposable.DisposeAsync"/>。
    /// </summary>
    /// <param name="cancellationToken">取消停止等待。</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
