namespace Zeus;

/// <summary>
/// 在一条通道上做请求-响应。串行发送，用超时等待下一帧完整应答；半包与粘包由编解码器消化。
/// 同一会话请勿并发调用请求方法，多设备共享通道时应自行排队。
/// </summary>
public sealed class FrameSession : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly IFrameCodec _codec;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _inboxLock = new();
    private readonly List<byte[]> _inbox = [];
    private PendingFrameRequest? _waiter;

    /// <summary>
    /// 创建会话并订阅通道接收事件。
    /// </summary>
    /// <param name="channel">已登记的传输通道。</param>
    /// <param name="codec">帧编解码器。每个会话独占一个实例。</param>
    /// <param name="timeout">等待应答的超时。默认 1 秒。</param>
    public FrameSession(IChannel channel, IFrameCodec codec, TimeSpan? timeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>会话绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>
    /// 发送一帧并等待下一帧应答。
    /// </summary>
    /// <param name="payload">业务载荷。</param>
    /// <param name="cancellationToken">取消等待。</param>
    /// <returns>应答载荷。</returns>
    public async Task<byte[]> RequestAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        => await RequestAsync(payload, static _ => true, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// 发送一帧并等待匹配的应答。适用于载荷内带序号、命令字或设备地址的私有协议。
    /// </summary>
    /// <param name="payload">业务载荷。</param>
    /// <param name="isExpectedResponse">返回 <c>true</c> 的完整载荷会作为本次应答；其它帧会留在收件箱中。</param>
    /// <param name="cancellationToken">取消等待。</param>
    /// <returns>匹配到的应答载荷。</returns>
    public async Task<byte[]> RequestAsync(
        ReadOnlyMemory<byte> payload,
        Func<ReadOnlyMemory<byte>, bool> isExpectedResponse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(isExpectedResponse);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var frame = _codec.Encode(payload.Span);
            var waiter = new PendingFrameRequest(isExpectedResponse);
            lock (_inboxLock)
            {
                if (TryTakeFromInbox(isExpectedResponse, out var cachedPayload))
                {
                    waiter.Completion.TrySetResult(cachedPayload);
                }
                else
                {
                    _waiter = waiter;
                }
            }

            try
            {
                await _channel.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_inboxLock)
                {
                    if (ReferenceEquals(_waiter, waiter))
                    {
                        _waiter = null;
                    }
                }

                throw;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);
            try
            {
                return await waiter.Completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lock (_inboxLock)
                {
                    if (ReferenceEquals(_waiter, waiter))
                    {
                        _waiter = null;
                    }
                }

                throw new ZeusProtocolException(
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到匹配应答。请检查从站是否在线、帧格式或序号匹配条件是否与对端一致。");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 只发送不期待应答，例如广播。
    /// </summary>
    /// <param name="payload">业务载荷。</param>
    /// <param name="cancellationToken">取消写入。</param>
    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _channel.WriteAsync(_codec.Encode(payload.Span), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _channel.DataReceived -= OnDataReceived;
        _channel.StateChanged -= OnStateChanged;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnDataReceived(object? sender, ChannelDataReceivedEventArgs e)
    {
        _codec.Append(e.Data.Span);
        while (_codec.TryDecode(out var payload))
        {
            PendingFrameRequest? waiter = null;
            lock (_inboxLock)
            {
                if (_waiter is not null && _waiter.IsExpectedResponse(payload))
                {
                    waiter = _waiter;
                    _waiter = null;
                }
                else
                {
                    _inbox.Add(payload);
                }
            }

            waiter?.Completion.TrySetResult(payload);
        }
    }

    private void OnStateChanged(object? sender, ChannelStateChangedEventArgs e)
    {
        if (e.Current is ChannelState.Faulted or ChannelState.Closed)
        {
            _codec.Reset();
            lock (_inboxLock)
            {
                _inbox.Clear();
                _waiter?.Completion.TrySetException(new ZeusProtocolException(
                    $"通道 {_channel.Name} 已变为 {e.Current}，未完成的请求已取消。"));
                _waiter = null;
            }
        }
    }

    private bool TryTakeFromInbox(Func<ReadOnlyMemory<byte>, bool> isExpectedResponse, out byte[] payload)
    {
        for (var i = 0; i < _inbox.Count; i++)
        {
            if (isExpectedResponse(_inbox[i]))
            {
                payload = _inbox[i];
                _inbox.RemoveAt(i);
                return true;
            }
        }

        payload = [];
        return false;
    }

    private sealed class PendingFrameRequest(Func<ReadOnlyMemory<byte>, bool> isExpectedResponse)
    {
        public Func<ReadOnlyMemory<byte>, bool> IsExpectedResponse { get; } = isExpectedResponse;

        public TaskCompletionSource<byte[]> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
