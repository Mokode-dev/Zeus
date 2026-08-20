namespace Zeus;

/// <summary>
/// 在一条通道上执行 Panasonic MEWTOCOL-COM 请求。同一客户端串行发送 ASCII 帧并校验 BCC。
/// </summary>
public sealed class MewtocolClient : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly MewtocolOptions _options;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private TaskCompletionSource<bool>? _dataPulse;

    /// <summary>创建 MEWTOCOL 客户端并订阅通道。</summary>
    public MewtocolClient(IChannel channel, MewtocolOptions? options = null, TimeSpan? timeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _options = CopyOptions(options ?? new MewtocolOptions());
        ValidateStationNumber(_options.StationNumber);
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>当前会话选项副本。</summary>
    public MewtocolOptions Options => CopyOptions(_options);

    /// <summary>执行任意 MEWTOCOL 命令。正常响应返回数据区；错误响应会抛出 <see cref="MewtocolException"/>。</summary>
    public async Task<string> ExecuteAsync(string command, string text, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            await _channel.WriteAsync(MewtocolCodec.EncodeRequest(_options.StationNumber, command, text), cancellationToken).ConfigureAwait(false);
            var response = await WaitForResponseAsync(command, cancellationToken).ConfigureAwait(false);
            if (response.StationNumber != _options.StationNumber)
            {
                throw new ZeusProtocolException(
                    $"MEWTOCOL 响应站号为 {response.StationNumber:00}，期望 {_options.StationNumber:00}。请避免多站共享同一客户端。");
            }

            if (response.IsError)
            {
                throw new MewtocolException(command, response.ErrorCode);
            }

            if (!string.Equals(response.Command, command, StringComparison.OrdinalIgnoreCase))
            {
                throw new ZeusProtocolException($"MEWTOCOL 响应命令为 {response.Command}，期望 {command}。");
            }

            return response.Text;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>读取 DT / LD / FL 数据寄存器字。</summary>
    public async Task<ushort[]> ReadDataWordsAsync(
        MewtocolDataArea area,
        int address,
        int count,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(
            MewtocolCodec.ReadData,
            MewtocolCodec.BuildReadDataWordsText(area, address, count),
            cancellationToken).ConfigureAwait(false);
        return MewtocolCodec.DecodeWordRead(response, count);
    }

    /// <summary>写入 DT / LD / FL 数据寄存器字。</summary>
    public async Task WriteDataWordsAsync(
        MewtocolDataArea area,
        int address,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(
            MewtocolCodec.WriteData,
            MewtocolCodec.BuildWriteDataWordsText(area, address, values),
            cancellationToken).ConfigureAwait(false);
        if (response.Length != 0)
        {
            throw new ZeusProtocolException("MEWTOCOL WD 写字响应数据区应为空。请核对 PLC 返回数据。");
        }
    }

    /// <summary>读取 X / Y / R / L 接点字块。</summary>
    public async Task<ushort[]> ReadContactWordsAsync(
        MewtocolContactArea area,
        int wordAddress,
        int count,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(
            MewtocolCodec.ReadContact,
            MewtocolCodec.BuildReadContactWordsText(area, wordAddress, count),
            cancellationToken).ConfigureAwait(false);
        return MewtocolCodec.DecodeWordRead(response, count);
    }

    /// <summary>写入 Y / R / L 接点字块。X 输入区通常由 PLC 侧驱动，不建议写入。</summary>
    public async Task WriteContactWordsAsync(
        MewtocolContactArea area,
        int wordAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(
            MewtocolCodec.WriteContact,
            MewtocolCodec.BuildWriteContactWordsText(area, wordAddress, values),
            cancellationToken).ConfigureAwait(false);
        if (response.Length != 0)
        {
            throw new ZeusProtocolException("MEWTOCOL WC 写接点字响应数据区应为空。请核对 PLC 返回数据。");
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

    private async Task<MewtocolResponseFrame> WaitForResponseAsync(string command, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            lock (_bufferLock)
            {
                if (MewtocolCodec.TryDecodeResponse(_buffer, out var response, out var consumed))
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
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 MEWTOCOL {command} 应答。请检查站号、串口参数、TCP 透传设置，或用 MewtocolSlaveResponder 联调。");
            }
        }
    }

    private void OnDataReceived(object? sender, ChannelDataReceivedEventArgs e)
    {
        lock (_bufferLock)
        {
            if (!ProtocolReceiveBuffer.TryAppend(_buffer, e.Data.Span, ProtocolReceiveBuffer.DefaultMaxBytes))
            {
                _dataPulse?.TrySetException(ProtocolReceiveBuffer.Overflow(_channel.Name, ProtocolReceiveBuffer.DefaultMaxBytes));
                _dataPulse = null;
                return;
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
                    $"通道 {_channel.Name} 已变为 {e.Current}，未完成的 MEWTOCOL 请求已取消。"));
                _dataPulse = null;
            }
        }
    }

    private static MewtocolOptions CopyOptions(MewtocolOptions source)
        => new()
        {
            StationNumber = source.StationNumber,
            WordOrder = source.WordOrder
        };

    private static void ValidateStationNumber(byte stationNumber)
    {
        if (stationNumber is < 1 or > 99)
        {
            throw new ZeusProtocolException($"MEWTOCOL 站号必须介于 1 与 99 之间，当前为 {stationNumber}。");
        }
    }
}
