namespace Zeus;

/// <summary>
/// 在一条通道上执行 Siemens S7 ISO-on-TCP 请求。同一客户端串行发送。
/// </summary>
public sealed class S7Client : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly S7Options _options;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private TaskCompletionSource<bool>? _dataPulse;
    private ushort _pduReference;
    private bool _sessionReady;

    /// <summary>
    /// 创建 S7 客户端并订阅通道。
    /// </summary>
    /// <param name="channel">传输通道，通常是 TCP 客户端通道，端口为 102。</param>
    /// <param name="options">S7 会话选项。省略时使用 rack 0 / slot 1。</param>
    /// <param name="timeout">应答超时，默认 1 秒。</param>
    public S7Client(IChannel channel, S7Options? options = null, TimeSpan? timeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _options = CopyOptions(options ?? new S7Options());
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>S7 会话选项。</summary>
    public S7Options Options => CopyOptions(_options);

    /// <summary>读取任意 S7 区域的连续字节。</summary>
    public Task<byte[]> ReadBytesAsync(
        S7Area area,
        int byteOffset,
        ushort length,
        int dbNumber = 0,
        CancellationToken cancellationToken = default)
        => ReadAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.Byte, length, cancellationToken);

    /// <summary>写入任意 S7 区域的连续字节。</summary>
    public Task WriteBytesAsync(
        S7Area area,
        int byteOffset,
        IReadOnlyList<byte> values,
        int dbNumber = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        return WriteAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.Byte, values.ToArray(), cancellationToken);
    }

    /// <summary>读取一个 Bool 点。</summary>
    public async Task<bool> ReadBoolAsync(
        S7Area area,
        int byteOffset,
        int bitOffset,
        int dbNumber = 0,
        CancellationToken cancellationToken = default)
    {
        var data = await ReadAreaAsync(area, dbNumber, byteOffset, bitOffset, S7DataType.Bool, 1, cancellationToken)
            .ConfigureAwait(false);
        return (bool)S7Codec.DecodeValue(S7DataType.Bool, data);
    }

    /// <summary>写入一个 Bool 点。</summary>
    public Task WriteBoolAsync(
        S7Area area,
        int byteOffset,
        int bitOffset,
        bool value,
        int dbNumber = 0,
        CancellationToken cancellationToken = default)
        => WriteAreaAsync(area, dbNumber, byteOffset, bitOffset, S7DataType.Bool, S7Codec.EncodeValue(S7DataType.Bool, value), cancellationToken);

    /// <summary>读取一个 Byte 点。</summary>
    public async Task<byte> ReadByteAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
    {
        var data = await ReadAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.Byte, 1, cancellationToken).ConfigureAwait(false);
        return (byte)S7Codec.DecodeValue(S7DataType.Byte, data);
    }

    /// <summary>写入一个 Byte 点。</summary>
    public Task WriteByteAsync(S7Area area, int byteOffset, byte value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => WriteAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.Byte, S7Codec.EncodeValue(S7DataType.Byte, value), cancellationToken);

    /// <summary>读取一个 Word 点。</summary>
    public async Task<ushort> ReadWordAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
    {
        var data = await ReadAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.Word, 2, cancellationToken).ConfigureAwait(false);
        return (ushort)S7Codec.DecodeValue(S7DataType.Word, data);
    }

    /// <summary>写入一个 Word 点。</summary>
    public Task WriteWordAsync(S7Area area, int byteOffset, ushort value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => WriteAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.Word, S7Codec.EncodeValue(S7DataType.Word, value), cancellationToken);

    /// <summary>读取一个 DWord 点。</summary>
    public async Task<uint> ReadDWordAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
    {
        var data = await ReadAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.DWord, 4, cancellationToken).ConfigureAwait(false);
        return (uint)S7Codec.DecodeValue(S7DataType.DWord, data);
    }

    /// <summary>写入一个 DWord 点。</summary>
    public Task WriteDWordAsync(S7Area area, int byteOffset, uint value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => WriteAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.DWord, S7Codec.EncodeValue(S7DataType.DWord, value), cancellationToken);

    /// <summary>读取一个 Int 点。</summary>
    public async Task<short> ReadIntAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
    {
        var data = await ReadAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.Int, 2, cancellationToken).ConfigureAwait(false);
        return (short)S7Codec.DecodeValue(S7DataType.Int, data);
    }

    /// <summary>写入一个 Int 点。</summary>
    public Task WriteIntAsync(S7Area area, int byteOffset, short value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => WriteAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.Int, S7Codec.EncodeValue(S7DataType.Int, value), cancellationToken);

    /// <summary>读取一个 DInt 点。</summary>
    public async Task<int> ReadDIntAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
    {
        var data = await ReadAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.DInt, 4, cancellationToken).ConfigureAwait(false);
        return (int)S7Codec.DecodeValue(S7DataType.DInt, data);
    }

    /// <summary>写入一个 DInt 点。</summary>
    public Task WriteDIntAsync(S7Area area, int byteOffset, int value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => WriteAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.DInt, S7Codec.EncodeValue(S7DataType.DInt, value), cancellationToken);

    /// <summary>读取一个 Real 点。</summary>
    public async Task<float> ReadRealAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
    {
        var data = await ReadAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.Real, 4, cancellationToken).ConfigureAwait(false);
        return (float)S7Codec.DecodeValue(S7DataType.Real, data);
    }

    /// <summary>写入一个 Real 点。</summary>
    public Task WriteRealAsync(S7Area area, int byteOffset, float value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => WriteAreaAsync(area, dbNumber, byteOffset, 0, S7DataType.Real, S7Codec.EncodeValue(S7DataType.Real, value), cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _channel.DataReceived -= OnDataReceived;
        _channel.StateChanged -= OnStateChanged;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    internal async Task<byte[]> ReadAreaAsync(
        S7Area area,
        int dbNumber,
        int byteOffset,
        int bitOffset,
        S7DataType dataType,
        int byteLength,
        CancellationToken cancellationToken)
    {
        if (byteLength <= 0 || byteLength > ushort.MaxValue)
        {
            throw new ZeusProtocolException($"S7 读取字节数必须在 1 到 65535 之间，当前为 {byteLength}。");
        }

        var address = S7Codec.CreateAddress(area, dbNumber, byteOffset, bitOffset, dataType);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            var pdu = NextPduReference();
            var requestAddress = dataType == S7DataType.Byte && byteLength != address.ByteLength
                ? new S7VariableAddress(area, dbNumber, byteOffset, bitOffset, dataType, byteLength)
                : address;
            await _channel.WriteAsync(S7Codec.EncodeReadVarRequest(pdu, [requestAddress]), cancellationToken).ConfigureAwait(false);
            return await WaitForFrameAsync(
                    frame => S7Codec.TryDecodeReadVarResponse(frame, pdu, 1, out var values) ? values[0] : null,
                    "S7 读取",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task WriteAreaAsync(
        S7Area area,
        int dbNumber,
        int byteOffset,
        int bitOffset,
        S7DataType dataType,
        byte[] value,
        CancellationToken cancellationToken)
    {
        var address = S7Codec.CreateAddress(area, dbNumber, byteOffset, bitOffset, dataType);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            var pdu = NextPduReference();
            var requestAddress = dataType == S7DataType.Byte && value.Length != address.ByteLength
                ? new S7VariableAddress(area, dbNumber, byteOffset, bitOffset, dataType, value.Length)
                : address;
            await _channel.WriteAsync(S7Codec.EncodeWriteVarRequest(pdu, [requestAddress], [value]), cancellationToken).ConfigureAwait(false);
            await WaitForFrameAsync<bool>(
                    frame => S7Codec.TryDecodeWriteVarResponse(frame, pdu, 1) ? true : null,
                    "S7 写入",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessionReady)
        {
            return;
        }

        lock (_bufferLock)
        {
            _buffer.Clear();
        }

        await _channel.WriteAsync(S7Codec.EncodeConnectionRequest(_options), cancellationToken).ConfigureAwait(false);
        await WaitForFrameAsync<bool>(
                frame => S7Codec.IsConnectionConfirm(frame) ? true : null,
                "S7 COTP 连接确认",
                cancellationToken)
            .ConfigureAwait(false);

        lock (_bufferLock)
        {
            _buffer.Clear();
        }

        var pdu = NextPduReference();
        await _channel.WriteAsync(S7Codec.EncodeSetupCommunicationRequest(pdu, _options.RequestedPduLength), cancellationToken).ConfigureAwait(false);
        await WaitForFrameAsync<bool>(
                frame => S7Codec.TryDecodeSetupCommunicationResponse(frame, pdu, out _) ? true : null,
                "S7 建立通信",
                cancellationToken)
            .ConfigureAwait(false);
        _sessionReady = true;
    }

    private async Task<T> WaitForFrameAsync<T>(Func<byte[], T?> decode, string operation, CancellationToken cancellationToken)
        where T : struct
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            lock (_bufferLock)
            {
                while (S7Codec.TryReadTpktFrame(_buffer, out var frame, out var consumed))
                {
                    _buffer.RemoveRange(0, consumed);
                    var result = decode(frame);
                    if (result.HasValue)
                    {
                        return result.Value;
                    }
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
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 {operation} 应答。请检查 PLC IP、端口 102、rack/slot，或用 S7SlaveResponder 联调。");
            }
        }
    }

    private async Task<byte[]> WaitForFrameAsync(Func<byte[], byte[]?> decode, string operation, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            lock (_bufferLock)
            {
                while (S7Codec.TryReadTpktFrame(_buffer, out var frame, out var consumed))
                {
                    _buffer.RemoveRange(0, consumed);
                    var result = decode(frame);
                    if (result is not null)
                    {
                        return result;
                    }
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
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 {operation} 应答。请检查 PLC IP、端口 102、rack/slot，或用 S7SlaveResponder 联调。");
            }
        }
    }

    private ushort NextPduReference()
    {
        _pduReference++;
        if (_pduReference == 0)
        {
            _pduReference = 1;
        }

        return _pduReference;
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
                _sessionReady = false;
                _buffer.Clear();
                _dataPulse?.TrySetException(new ZeusProtocolException(
                    $"通道 {_channel.Name} 已变为 {e.Current}，未完成的 S7 请求已取消。"));
                _dataPulse = null;
            }
        }
    }

    private static S7Options CopyOptions(S7Options source)
        => new()
        {
            Rack = source.Rack,
            Slot = source.Slot,
            LocalTsap = source.LocalTsap,
            RemoteTsap = source.RemoteTsap,
            RequestedPduLength = source.RequestedPduLength
        };
}
