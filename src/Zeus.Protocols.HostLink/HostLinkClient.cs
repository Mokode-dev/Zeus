namespace Zeus;

/// <summary>
/// 在一条通道上执行 Omron Host Link 请求。同一客户端串行发送 ASCII 帧并校验 FCS。
/// </summary>
public sealed class HostLinkClient : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly HostLinkOptions _options;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private TaskCompletionSource<bool>? _dataPulse;

    /// <summary>创建 Host Link 客户端并订阅通道。</summary>
    public HostLinkClient(IChannel channel, HostLinkOptions? options = null, TimeSpan? timeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _options = CopyOptions(options ?? new HostLinkOptions());
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>当前会话选项副本。</summary>
    public HostLinkOptions Options => CopyOptions(_options);

    /// <summary>执行任意 Host Link 命令。返回数据区不含结束码；非零结束码会抛出 <see cref="HostLinkException"/>。</summary>
    public async Task<string> ExecuteAsync(string command, string text, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            await _channel.WriteAsync(HostLinkCodec.EncodeRequest(_options.UnitNumber, command, text), cancellationToken).ConfigureAwait(false);
            var response = await WaitForResponseAsync(command, cancellationToken).ConfigureAwait(false);
            if (response.UnitNumber != _options.UnitNumber)
            {
                throw new ZeusProtocolException(
                    $"Host Link 响应单元号为 {response.UnitNumber:00}，期望 {_options.UnitNumber:00}。请避免多站共享同一客户端。");
            }

            if (!string.Equals(response.Command, command, StringComparison.OrdinalIgnoreCase))
            {
                throw new ZeusProtocolException($"Host Link 响应命令为 {response.Command}，期望 {command}。");
            }

            if (response.EndCode != 0)
            {
                throw new HostLinkException(command, response.EndCode);
            }

            return response.Text;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>读取字区。</summary>
    public async Task<ushort[]> ReadWordsAsync(
        HostLinkArea area,
        ushort address,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(
            HostLinkCodec.ReadCommand(area),
            HostLinkCodec.BuildReadWordsText(address, count),
            cancellationToken).ConfigureAwait(false);
        return HostLinkCodec.DecodeWordRead(response, count);
    }

    /// <summary>写入字区。</summary>
    public async Task WriteWordsAsync(
        HostLinkArea area,
        ushort address,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(
            HostLinkCodec.WriteCommand(area),
            HostLinkCodec.BuildWriteWordsText(address, values),
            cancellationToken).ConfigureAwait(false);
        if (response.Length != 0)
        {
            throw new ZeusProtocolException("Host Link 写字响应数据区应为空。请核对 PLC 返回数据。");
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

    private async Task<HostLinkResponseFrame> WaitForResponseAsync(string command, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            lock (_bufferLock)
            {
                if (HostLinkCodec.TryDecodeResponse(_buffer, out var response, out var consumed))
                {
                    _buffer.RemoveRange(0, consumed);
                    return response;
                }

                _dataPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            try
            {
                await _dataPulse.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ZeusProtocolException(
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 Host Link {command} 应答。请检查单元号、串口参数、TCP 透传设置，或用 HostLinkSlaveResponder 联调。");
            }
        }
    }

    private void OnDataReceived(object? sender, ChannelDataReceivedEventArgs e)
    {
        lock (_bufferLock)
        {
            foreach (var value in e.Data.Span)
            {
                _buffer.Add(value);
            }

            _dataPulse?.TrySetResult(true);
            _dataPulse = null;
        }
    }

    private void OnStateChanged(object? sender, ChannelStateChangedEventArgs e)
    {
        if (e.Current is ChannelState.Faulted or ChannelState.Closed)
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
                _dataPulse?.TrySetException(new ZeusProtocolException(
                    $"通道 {_channel.Name} 已变为 {e.Current}，未完成的 Host Link 请求已取消。"));
                _dataPulse = null;
            }
        }
    }

    private static HostLinkOptions CopyOptions(HostLinkOptions source)
        => new()
        {
            UnitNumber = source.UnitNumber,
            WordOrder = source.WordOrder
        };
}
