using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 通道公共状态机。
/// 具体传输只需实现打开、关闭与写入三步，打开失败、重复开关与事件发布由本基类消化。
/// </summary>
public abstract class ChannelBase : IChannel
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _logger;
    private ChannelState _state = ChannelState.Created;
    private bool _disposed;

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
    public ChannelState State => _state;

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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == ChannelState.Open)
            {
                return;
            }

            SetState(ChannelState.Opening);
            try
            {
                await OpenCoreAsync(cancellationToken).ConfigureAwait(false);
                SetState(ChannelState.Open);
                _logger.LogInformation("通道 {Channel} 已打开。", Name);
            }
            catch (Exception ex)
            {
                SetState(ChannelState.Faulted, ex);
                throw new ZeusChannelException(
                    Name,
                    $"通道 {Name} 打开失败：{ex.Message}。可改用虚拟通道联调，或检查端口占用与权限。",
                    ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state is ChannelState.Closed or ChannelState.Created)
            {
                _state = ChannelState.Closed;
                return;
            }

            try
            {
                await CloseCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "通道 {Channel} 关闭时出现异常，仍将标记为已关闭。", Name);
            }

            SetState(ChannelState.Closed);
            _logger.LogInformation("通道 {Channel} 已关闭。", Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_state != ChannelState.Open)
        {
            throw new ZeusChannelException(
                Name,
                $"通道 {Name} 当前为 {_state}，无法写入。请先调用宿主 StartAsync，或检查该通道是否已故障。");
        }

        try
        {
            await WriteCoreAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ZeusException)
        {
            SetState(ChannelState.Faulted, ex);
            throw new ZeusChannelException(Name, $"通道 {Name} 写入失败：{ex.Message}。", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Dispose();
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
    /// </summary>
    /// <param name="cancellationToken">取消关闭等待。</param>
    protected abstract Task CloseCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 向底层写入字节。
    /// </summary>
    /// <param name="buffer">待发送数据。</param>
    /// <param name="cancellationToken">取消写入。</param>
    protected abstract Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>
    /// 由具体传输在收到数据后调用。基类会复制载荷再发布事件，避免订阅方看到内部缓冲。
    /// </summary>
    /// <param name="data">本次收到的字节。</param>
    protected void PublishData(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        var copy = data.ToArray();
        PublishPacketTrace(ChannelTraceDirection.Received, copy);
        DataReceived?.Invoke(this, new ChannelDataReceivedEventArgs(copy));
    }

    /// <summary>
    /// 发布通道报文追踪事件。具体传输在确认写入已提交后调用；接收方向由 <see cref="PublishData"/> 统一处理。
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
        var previous = _state;
        if (previous == next && error is null)
        {
            return;
        }

        _state = next;
        StateChanged?.Invoke(this, new ChannelStateChangedEventArgs(previous, next, error));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(Name, $"通道 {Name} 已释放，不能再打开或写入。");
        }
    }
}
