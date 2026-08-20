using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 通道公共状态机。
/// 具体传输只需实现打开、关闭与写入三步，打开失败、重复开关与事件发布由本基类消化。
/// 写入与开关使用两把锁：关闭会排空在途写入，避免与套接字释放交错。
/// </summary>
public abstract class ChannelBase : IChannel
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ILogger _logger;
    private int _state = (int)ChannelState.Created;
    private int _disposed;

    /// <summary>
    /// 初始化通道基类。
    /// </summary>
    /// <param name="name">宿主内唯一名称。</param>
    /// <param name="logger">诊断日志。允许为 <c>null</c>，此时使用空记录器。</param>
    protected ChannelBase(string name, ILogger? logger)
    {
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public ChannelState State => (ChannelState)Volatile.Read(ref _state);

    /// <inheritdoc />
    public event EventHandler<ChannelStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<ChannelDataReceivedEventArgs>? DataReceived;

    /// <inheritdoc />
    public event EventHandler<ChannelTraceEventArgs>? PacketTraced;

    /// <inheritdoc />
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State == ChannelState.Open)
            {
                return;
            }

            // 关闭或故障后再开：先尽力释放残留句柄，避免串口/套接字半开。
            if (State is ChannelState.Closed or ChannelState.Faulted)
            {
                await CloseCoreQuietlyAsync(cancellationToken).ConfigureAwait(false);
            }

            SetState(ChannelState.Opening);
            try
            {
                await OpenCoreAsync(cancellationToken).ConfigureAwait(false);
                SetState(ChannelState.Open);
                using var scope = BeginChannelScope();
                _logger.LogInformation(ZeusLogEvents.ChannelOpened, "通道 {Channel} 已打开。", Name);
            }
            catch (Exception ex)
            {
                await CloseCoreQuietlyAsync(CancellationToken.None).ConfigureAwait(false);
                SetState(ChannelState.Faulted, ex);
                throw new ZeusChannelException(
                    Name,
                    $"通道 {Name} 打开失败：{ex.Message}。可改用虚拟通道联调，或检查端口占用与权限。",
                    ex);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is ChannelState.Closed or ChannelState.Created)
            {
                SetState(ChannelState.Closed);
                return;
            }

            // 排空在途写入后再关底层，避免 WriteAsync 与 Dispose 套接字并发。
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await CloseCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                using var scope = BeginChannelScope();
                _logger.LogWarning(ZeusLogEvents.ChannelCloseWarning, ex, "通道 {Channel} 关闭时出现异常，仍将标记为已关闭。", Name);
            }
            finally
            {
                _writeGate.Release();
            }

            SetState(ChannelState.Closed);
            using (BeginChannelScope())
            {
                _logger.LogInformation(ZeusLogEvents.ChannelClosed, "通道 {Channel} 已关闭。", Name);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (State != ChannelState.Open)
        {
            throw new ZeusChannelException(
                Name,
                $"通道 {Name} 当前为 {State}，无法写入。请先调用宿主 StartAsync，或检查该通道是否已故障。");
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State != ChannelState.Open)
            {
                throw new ZeusChannelException(
                    Name,
                    $"通道 {Name} 当前为 {State}，无法写入。请先调用宿主 StartAsync，或检查该通道是否已故障。");
            }

            try
            {
                await WriteCoreAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not ZeusException)
            {
                SetState(ChannelState.Faulted, ex);
                using var scope = BeginChannelScope();
                _logger.LogWarning(ZeusLogEvents.ChannelWriteFailed, ex, "通道 {Channel} 写入失败。", Name);
                throw new ZeusChannelException(Name, $"通道 {Name} 写入失败：{ex.Message}。", ex);
            }
        }
        finally
        {
            _writeGate.Release();
        }

        // 写锁已释放后再发布延迟接收，避免虚拟通道在持锁栈上同步回写导致协议层自死锁。
        FlushDeferredReceive();
    }

    /// <summary>
    /// 在写锁内执行自定义写入。TCP/UDP 服务端按远端发送时使用，避免与默认 <see cref="WriteAsync"/> 交错。
    /// </summary>
    /// <param name="write">持锁期间执行的写入。</param>
    /// <param name="cancellationToken">取消写入。</param>
    protected async Task WriteExclusiveAsync(Func<CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        ThrowIfDisposed();
        if (State != ChannelState.Open)
        {
            throw new ZeusChannelException(
                Name,
                $"通道 {Name} 当前为 {State}，无法写入。请先调用宿主 StartAsync，或检查该通道是否已故障。");
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State != ChannelState.Open)
            {
                throw new ZeusChannelException(
                    Name,
                    $"通道 {Name} 当前为 {State}，无法写入。请先调用宿主 StartAsync，或检查该通道是否已故障。");
            }

            try
            {
                await write(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not ZeusException)
            {
                SetState(ChannelState.Faulted, ex);
                using var scope = BeginChannelScope();
                _logger.LogWarning(ZeusLogEvents.ChannelWriteFailed, ex, "通道 {Channel} 写入失败。", Name);
                throw new ZeusChannelException(Name, $"通道 {Name} 写入失败：{ex.Message}。", ex);
            }
        }
        finally
        {
            _writeGate.Release();
        }

        FlushDeferredReceive();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Dispose();
            _writeGate.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 打开底层传输。失败时由基类转为 <see cref="ChannelState.Faulted"/>。
    /// </summary>
    /// <param name="cancellationToken">取消打开。</param>
    protected abstract Task OpenCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 关闭底层传输。即使抛出异常，基类仍会将状态置为已关闭。
    /// 从故障态重新打开前也会调用，实现必须可重复执行。
    /// </summary>
    /// <param name="cancellationToken">取消关闭等待。</param>
    protected abstract Task CloseCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 向底层写入字节。调用期间持有写锁，实现不得再进入 <see cref="WriteAsync"/>。
    /// </summary>
    /// <param name="buffer">待发送数据。</param>
    /// <param name="cancellationToken">取消写入。</param>
    protected abstract Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>
    /// 写锁释放后调用。虚拟通道在此发布回显，避免在持锁栈上同步触发 <see cref="DataReceived"/>。
    /// </summary>
    protected virtual void FlushDeferredReceive()
    {
    }

    /// <summary>
    /// 由具体传输在收到数据后调用。基类会复制载荷再发布事件，避免订阅方看到内部缓冲。
    /// </summary>
    /// <param name="data">本次收到的字节。</param>
    protected void PublishData(ReadOnlySpan<byte> data)
        => PublishData(data, null);

    /// <summary>
    /// 由具体传输在收到带远端的数据后调用。TCP/UDP 服务端应传入对端，便于按会话回写。
    /// </summary>
    /// <param name="data">本次收到的字节。</param>
    /// <param name="remoteEndPoint">发送本段数据的远端。</param>
    protected void PublishData(ReadOnlySpan<byte> data, System.Net.EndPoint? remoteEndPoint)
    {
        if (data.IsEmpty)
        {
            return;
        }

        var copy = data.ToArray();
        PublishPacketTrace(ChannelTraceDirection.Received, copy);
        DataReceived?.Invoke(this, new ChannelDataReceivedEventArgs(copy, remoteEndPoint));
    }

    /// <summary>
    /// 热重载移除本实例前拷出事件订阅，供同名新通道接续。
    /// </summary>
    internal CapturedSubscriptions CaptureSubscriptions()
    {
        var captured = new CapturedSubscriptions(StateChanged, DataReceived, PacketTraced);
        StateChanged = null;
        DataReceived = null;
        PacketTraced = null;
        return captured;
    }

    /// <summary>
    /// 把旧实例上的事件订阅接到本通道。已有订阅排在前面，避免覆盖新代码刚挂上的处理程序。
    /// </summary>
    internal void RestoreSubscriptions(CapturedSubscriptions subscriptions)
    {
        if (subscriptions.StateChanged is not null)
        {
            StateChanged = subscriptions.StateChanged + StateChanged;
        }

        if (subscriptions.DataReceived is not null)
        {
            DataReceived = subscriptions.DataReceived + DataReceived;
        }

        if (subscriptions.PacketTraced is not null)
        {
            PacketTraced = subscriptions.PacketTraced + PacketTraced;
        }
    }

    /// <summary>
    /// 发布通道报文追踪事件。具体传输在确认写入已提交后调用；接收方向由 <see cref="PublishData(ReadOnlySpan{byte})"/> 统一处理。
    /// </summary>
    /// <param name="direction">报文方向。</param>
    /// <param name="data">报文字节。</param>
    protected void PublishPacketTrace(ChannelTraceDirection direction, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        var copy = data.ToArray();
        PacketTraced?.Invoke(this, new ChannelTraceEventArgs(direction, copy, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// 推进状态并发布 <see cref="StateChanged"/>。相同状态且无新异常时不重复通知。
    /// </summary>
    /// <param name="next">目标状态。</param>
    /// <param name="error">可选故障原因。</param>
    protected void SetState(ChannelState next, Exception? error = null)
    {
        var previous = (ChannelState)Volatile.Read(ref _state);
        if (previous == next && error is null)
        {
            return;
        }

        Volatile.Write(ref _state, (int)next);
        StateChanged?.Invoke(this, new ChannelStateChangedEventArgs(previous, next, error));
    }

    /// <summary>
    /// 尽力关闭底层传输并吞掉异常。重开前清理残留资源时使用。
    /// </summary>
    private async Task CloseCoreQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CloseCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            using var scope = BeginChannelScope();
            _logger.LogDebug(ZeusLogEvents.ChannelCleanup, ex, "通道 {Channel} 重开前清理传输资源时出现异常，将继续尝试打开。", Name);
        }
    }

    /// <summary>打开带通道名的日志作用域。</summary>
    private IDisposable BeginChannelScope() => LogScope.Begin(_logger, "Channel", Name);

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(Name, $"通道 {Name} 已释放，不能再打开或写入。");
        }
    }
}
