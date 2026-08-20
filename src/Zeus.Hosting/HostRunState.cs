namespace Zeus;

/// <summary>
/// 宿主运行闸门。Generic Host 在首次启动后保持存活；
/// 用户调用 <see cref="IZeusHost.StopAsync"/> 只暂停采集与重连并关闭通道，以便再次 <see cref="IZeusHost.StartAsync"/>。
/// </summary>
internal sealed class HostRunState
{
    private readonly object _gate = new();
    private TaskCompletionSource _runningSignal = CreateSignal();
    private CancellationTokenSource _pauseCts = new();

    /// <summary>用户是否已启动宿主（通道应打开）。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 暂停令牌。运行中为未取消；停止后取消，采集延时会提前结束。
    /// </summary>
    public CancellationToken PauseToken
    {
        get
        {
            lock (_gate)
            {
                return _pauseCts.Token;
            }
        }
    }

    /// <summary>从停止转为运行时触发。</summary>
    public event EventHandler? Started;

    /// <summary>从运行转为停止时触发。</summary>
    public event EventHandler? Stopped;

    /// <summary>
    /// 标记为已运行。重复调用忽略。
    /// </summary>
    public void MarkStarted()
    {
        var raise = false;
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            IsRunning = true;
            var previousPause = _pauseCts;
            _pauseCts = new CancellationTokenSource();
            previousPause.Dispose();
            _runningSignal.TrySetResult();
            raise = true;
        }

        if (raise)
        {
            Started?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 标记为已停止。重复调用忽略。
    /// </summary>
    public void MarkStopped()
    {
        var raise = false;
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            _pauseCts.Cancel();
            _runningSignal = CreateSignal();
            raise = true;
        }

        if (raise)
        {
            Stopped?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 若宿主已停止则等待再次启动；已运行则立即返回。
    /// </summary>
    /// <param name="cancellationToken">取消等待，通常来自 Generic Host 关闭。</param>
    public async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task wait;
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            wait = _runningSignal.Task;
        }

        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TaskCompletionSource CreateSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
