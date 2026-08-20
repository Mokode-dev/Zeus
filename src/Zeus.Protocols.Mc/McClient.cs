namespace Zeus;

/// <summary>
/// 在一条通道上执行 Mitsubishi MC Protocol 请求。同一客户端串行发送。
/// </summary>
public sealed class McClient : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly Mc3EOptions _options;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private TaskCompletionSource<bool>? _dataPulse;

    /// <summary>
    /// 创建 MC 客户端并订阅通道。
    /// </summary>
    /// <param name="channel">传输通道，通常是 TCP 客户端通道。</param>
    /// <param name="options">MC 帧选项。省略时使用 3E Binary 常见默认值。</param>
    /// <param name="timeout">应答超时，默认 1 秒。</param>
    public McClient(IChannel channel, Mc3EOptions? options = null, TimeSpan? timeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _options = CopyOptions(options ?? new Mc3EOptions());
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>MC 帧选项。</summary>
    public Mc3EOptions Options => CopyOptions(_options);

    /// <summary>
    /// 发送 MC 命令并返回响应数据区。非零结束码会抛出 <see cref="McException"/>。
    /// </summary>
    public async Task<byte[]> ExecuteAsync(
        ushort command,
        ushort subcommand,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            var request = Mc3ECodec.EncodeRequest(_options, command, subcommand, data.Span);
            await _channel.WriteAsync(request, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            while (true)
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                lock (_bufferLock)
                {
                    if (Mc3ECodec.TryDecodeRawResponse(_buffer, _options, out var endCode, out var response, out var consumed))
                    {
                        _buffer.RemoveRange(0, consumed);
                        if (endCode != 0)
                        {
                            throw new McException(endCode);
                        }

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
                        $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 MC 3E 应答。请检查 PLC IP、端口、3E 帧设置或用 McSlaveResponder 联调。");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>批量读取字软元件，例如 D 寄存器。</summary>
    public async Task<ushort[]> ReadWordsAsync(
        McDeviceCode deviceCode,
        int address,
        ushort points,
        CancellationToken cancellationToken = default)
    {
        EnsurePoints(points, GetMaxWordPoints(), "MC 字软元件读取");
        var request = Mc3ECodec.BuildDeviceRequest(address, deviceCode, points);
        var response = await ExecuteDeviceAsync(McOperation.ReadWords, request, points, cancellationToken)
            .ConfigureAwait(false);

        if (response.Length < points * 2)
        {
            throw new ZeusProtocolException("MC 字软元件读取响应长度不足。请核对 PLC 返回数据。");
        }

        var values = new ushort[points];
        for (var i = 0; i < points; i++)
        {
            values[i] = Mc3ECodec.ReadUInt16LittleEndian(response.AsSpan(i * 2, 2));
        }

        return values;
    }

    /// <summary>批量写入字软元件，例如 D 寄存器。</summary>
    public async Task WriteWordsAsync(
        McDeviceCode deviceCode,
        int address,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        EnsurePoints(values.Count, GetMaxWordPoints(), "MC 字软元件写入");
        var request = new byte[6 + (values.Count * 2)];
        Mc3ECodec.BuildDeviceRequest(address, deviceCode, (ushort)values.Count).CopyTo(request, 0);
        for (var i = 0; i < values.Count; i++)
        {
            Mc3ECodec.WriteUInt16LittleEndian(request.AsSpan(6 + (i * 2), 2), values[i]);
        }

        var response = await ExecuteDeviceAsync(McOperation.WriteWords, request, (ushort)values.Count, cancellationToken)
            .ConfigureAwait(false);
        if (response.Length != 0)
        {
            throw new ZeusProtocolException("MC 字软元件写入响应数据区应为空。请核对 PLC 返回数据。");
        }
    }

    /// <summary>批量读取位软元件，例如 M 继电器。</summary>
    public async Task<bool[]> ReadBitsAsync(
        McDeviceCode deviceCode,
        int address,
        ushort points,
        CancellationToken cancellationToken = default)
    {
        EnsurePoints(points, GetMaxBitPoints(), "MC 位软元件读取");
        var request = Mc3ECodec.BuildDeviceRequest(address, deviceCode, points);
        var response = await ExecuteDeviceAsync(McOperation.ReadBits, request, points, cancellationToken)
            .ConfigureAwait(false);

        var byteCount = Mc3ECodec.BitByteCount(points);
        if (response.Length < byteCount)
        {
            throw new ZeusProtocolException("MC 位软元件读取响应长度不足。请核对 PLC 返回数据。");
        }

        var values = new bool[points];
        for (var i = 0; i < points; i++)
        {
            values[i] = Mc3ECodec.GetPackedBit(response, i);
        }

        return values;
    }

    /// <summary>随机读取单字/双字软元件。仅 3E/4E 帧支持。</summary>
    public async Task<McRandomReadResult> ReadRandomAsync(
        IReadOnlyList<McDeviceAddress> wordDevices,
        IReadOnlyList<McDeviceAddress>? doubleWordDevices = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wordDevices);
        doubleWordDevices ??= Array.Empty<McDeviceAddress>();
        EnsureRandomCounts(wordDevices.Count, doubleWordDevices.Count, "MC 随机读取");
        var request = Mc3ECodec.BuildRandomReadRequest(wordDevices, doubleWordDevices);
        var response = await ExecuteDeviceAsync(
                McOperation.RandomRead,
                request,
                (ushort)wordDevices.Count,
                cancellationToken,
                (ushort)doubleWordDevices.Count)
            .ConfigureAwait(false);

        return Mc3ECodec.ReadRandomReadResponse(response, (ushort)wordDevices.Count, (ushort)doubleWordDevices.Count);
    }

    /// <summary>批量写入位软元件，例如 M 继电器。</summary>
    public async Task WriteBitsAsync(
        McDeviceCode deviceCode,
        int address,
        IReadOnlyList<bool> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        EnsurePoints(values.Count, GetMaxBitPoints(), "MC 位软元件写入");
        var request = new byte[6 + Mc3ECodec.BitByteCount(values.Count)];
        Mc3ECodec.BuildDeviceRequest(address, deviceCode, (ushort)values.Count).CopyTo(request, 0);
        for (var i = 0; i < values.Count; i++)
        {
            Mc3ECodec.SetPackedBit(request.AsSpan(6), i, values[i]);
        }

        var response = await ExecuteDeviceAsync(McOperation.WriteBits, request, (ushort)values.Count, cancellationToken)
            .ConfigureAwait(false);
        if (response.Length != 0)
        {
            throw new ZeusProtocolException("MC 位软元件写入响应数据区应为空。请核对 PLC 返回数据。");
        }
    }

    /// <summary>随机写入单字/双字软元件。仅 3E/4E 帧支持。</summary>
    public async Task WriteRandomWordsAsync(
        IReadOnlyList<McWordWrite> wordValues,
        IReadOnlyList<McDoubleWordWrite>? doubleWordValues = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wordValues);
        doubleWordValues ??= Array.Empty<McDoubleWordWrite>();
        EnsureRandomCounts(wordValues.Count, doubleWordValues.Count, "MC 随机写入字软元件");
        var request = Mc3ECodec.BuildRandomWriteWordsRequest(wordValues, doubleWordValues);
        var response = await ExecuteDeviceAsync(
                McOperation.RandomWriteWords,
                request,
                (ushort)wordValues.Count,
                cancellationToken,
                (ushort)doubleWordValues.Count)
            .ConfigureAwait(false);
        if (response.Length != 0)
        {
            throw new ZeusProtocolException("MC 随机写入字软元件响应数据区应为空。请核对 PLC 返回数据。");
        }
    }

    /// <summary>随机写入位软元件。仅 3E/4E 帧支持。</summary>
    public async Task WriteRandomBitsAsync(
        IReadOnlyList<McBitWrite> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        EnsureRandomCount(values.Count, "MC 随机写入位软元件");
        var request = Mc3ECodec.BuildRandomWriteBitsRequest(values);
        var response = await ExecuteDeviceAsync(
                McOperation.RandomWriteBits,
                request,
                (ushort)values.Count,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.Length != 0)
        {
            throw new ZeusProtocolException("MC 随机写入位软元件响应数据区应为空。请核对 PLC 返回数据。");
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

    private static void EnsurePoints(int points, int max, string operation)
    {
        if (points <= 0 || points > max)
        {
            throw new ZeusProtocolException($"{operation}点数必须在 1 到 {max} 之间，当前为 {points}。");
        }
    }

    private static void EnsureRandomCounts(int wordCount, int doubleWordCount, string operation)
    {
        EnsureRandomCount(wordCount, $"{operation}单字软元件", allowZero: true);
        EnsureRandomCount(doubleWordCount, $"{operation}双字软元件", allowZero: true);
        if (wordCount == 0 && doubleWordCount == 0)
        {
            throw new ZeusProtocolException($"{operation}至少需要 1 个软元件。");
        }
    }

    private static void EnsureRandomCount(int count, string operation, bool allowZero = false)
    {
        if ((count == 0 && allowZero) || count is >= 1 and <= byte.MaxValue)
        {
            return;
        }

        var min = allowZero ? 0 : 1;
        throw new ZeusProtocolException($"{operation}数量必须在 {min} 到 {byte.MaxValue} 之间，当前为 {count}。");
    }

    private static Mc3EOptions CopyOptions(Mc3EOptions source)
        => new()
        {
            FrameType = source.FrameType,
            DataEncoding = source.DataEncoding,
            SerialNumber = source.SerialNumber,
            NetworkNumber = source.NetworkNumber,
            PcNumber = source.PcNumber,
            IoNumber = source.IoNumber,
            StationNumber = source.StationNumber,
            MonitoringTimer = source.MonitoringTimer
        };

    private async Task<byte[]> ExecuteDeviceAsync(
        McOperation operation,
        ReadOnlyMemory<byte> data,
        ushort points,
        CancellationToken cancellationToken,
        ushort extraPoints = 0)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            var request = Mc3ECodec.EncodeDeviceRequest(_options, operation, data.Span);
            var pending = Mc3ECodec.CreatePending(operation, points, extraPoints);
            await _channel.WriteAsync(request, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            while (true)
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                lock (_bufferLock)
                {
                    if (Mc3ECodec.TryDecodeDeviceResponse(_buffer, _options, pending, out var endCode, out var response, out var consumed))
                    {
                        _buffer.RemoveRange(0, consumed);
                        if (endCode != 0)
                        {
                            throw new McException(endCode);
                        }

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
                        $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 MC 应答。请检查 PLC IP、端口、帧类型、编码或用 McSlaveResponder 联调。");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private int GetMaxWordPoints()
        => _options.FrameType == McFrameType.Frame1E ? 256 : 960;

    private int GetMaxBitPoints()
        => _options.FrameType == McFrameType.Frame1E
            ? 256
            : _options.DataEncoding == McDataEncoding.Ascii ? 3584 : 7168;

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
                    $"通道 {_channel.Name} 已变为 {e.Current}，未完成的 MC 请求已取消。"));
                _dataPulse = null;
            }
        }
    }
}
