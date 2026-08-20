namespace Zeus;

/// <summary>
/// 把宿主启停挂到窗口生命周期上时使用的一次性开关。
/// 窗体可能重复触发 Loaded/Closed，本类型保证 Start/Dispose 各自最多执行一次，避免重复打开串口。
/// </summary>
public sealed class UiHostAttachment : IAsyncDisposable
{
    private readonly IZeusHost _host;
    private int _started;
    private int _stopped;

    /// <summary>
    /// 包装一个尚未启动的宿主。
    /// </summary>
    /// <param name="host">由 <c>ZeusHost.Create</c> 得到的实例。</param>
    public UiHostAttachment(IZeusHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>被包装的宿主，供查找通道与设备。</summary>
    public IZeusHost Host => _host;

    /// <summary>
    /// 启动宿主。重复调用会被忽略。
    /// </summary>
    /// <param name="cancellationToken">取消启动。</param>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return Task.CompletedTask;
        }

        return _host.StartAsync(cancellationToken);
    }

    /// <summary>
    /// 停止并释放宿主。重复调用会被忽略。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        await _host.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 在后台释放宿主，避免在 UI 线程上同步等待通道关闭造成卡死。
    /// 窗口关闭事件无法方便地 await 时使用。
    /// </summary>
    public void DisposeBlocking()
    {
        _ = DisposeAsync();
    }
}
