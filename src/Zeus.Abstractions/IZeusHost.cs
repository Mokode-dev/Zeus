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

    /// <summary>底层服务提供者，高级场景可由此解析自定义服务。</summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// 启动宿主：按注册顺序打开通道并启动后台服务。
    /// </summary>
    /// <param name="cancellationToken">取消启动。</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止宿主：停止后台服务并按相反顺序关闭通道。
    /// </summary>
    /// <param name="cancellationToken">取消停止等待。</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
