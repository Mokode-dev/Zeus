namespace Zeus;

/// <summary>
/// 在一条通道上执行 Omron FINS 请求。同一客户端串行发送，支持 FINS/UDP 与 FINS/TCP。
/// </summary>
public sealed class FinsClient : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly FinsTransport _transport;
    private readonly FinsOptions _options;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private TaskCompletionSource<bool>? _dataPulse;
    private byte _serviceId;
    private bool _tcpHandshakeReady;

    /// <summary>
    /// 创建 FINS 客户端并订阅通道。
    /// </summary>
    public FinsClient(IChannel channel, FinsTransport transport, FinsOptions? options = null, TimeSpan? timeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _transport = transport;
        _options = CopyOptions(options ?? new FinsOptions());
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>线上封装。</summary>
    public FinsTransport Transport => _transport;

    /// <summary>当前会话选项副本。FINS/TCP 握手成功后 SourceNode / DestinationNode 会反映协商结果。</summary>
    public FinsOptions Options => CopyOptions(_options);

    /// <summary>
    /// 执行任意 FINS 命令。返回数据区不含命令码与结束码；非零结束码会抛出 <see cref="FinsException"/>。
    /// </summary>
    public async Task<byte[]> ExecuteAsync(ushort command, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_transport == FinsTransport.Tcp && _options.UseTcpNodeAddressHandshake && !_tcpHandshakeReady)
            {
                await ExecuteTcpHandshakeAsync(cancellationToken).ConfigureAwait(false);
            }

            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            var sid = NextServiceId();
            var fins = FinsCodec.EncodeRequestFrame(_options, sid, command, data.Span);
            var packet = _transport == FinsTransport.Tcp
                ? FinsCodec.EncodeTcpPacket(FinsCodec.TcpCommandFinsFrameSend, 0, fins)
                : fins;
            await _channel.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
            var response = await WaitForResponseAsync(command, sid, cancellationToken).ConfigureAwait(false);
            if (response.EndCode != 0)
            {
                throw new FinsException(command, response.EndCode);
            }

            return response.Data;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>读取字区。</summary>
    public async Task<ushort[]> ReadWordsAsync(
        FinsMemoryAreaCode area,
        ushort address,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        var request = FinsCodec.BuildMemoryAreaReadRequest(area, address, 0, count);
        var response = await ExecuteAsync(FinsCodec.MemoryAreaRead, request, cancellationToken).ConfigureAwait(false);
        return FinsCodec.DecodeWordRead(response, count);
    }

    /// <summary>写入字区。</summary>
    public async Task WriteWordsAsync(
        FinsMemoryAreaCode area,
        ushort address,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        var request = FinsCodec.BuildMemoryAreaWriteRequest(area, address, 0, values);
        var response = await ExecuteAsync(FinsCodec.MemoryAreaWrite, request, cancellationToken).ConfigureAwait(false);
        if (response.Length != 0)
        {
            throw new ZeusProtocolException("FINS 写字响应数据区应为空。请核对 PLC 返回数据。");
        }
    }

    /// <summary>读取位区。</summary>
    public async Task<bool[]> ReadBitsAsync(
        FinsMemoryAreaCode area,
        ushort address,
        byte bitOffset,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        var request = FinsCodec.BuildMemoryAreaReadRequest(area, address, bitOffset, count);
        var response = await ExecuteAsync(FinsCodec.MemoryAreaRead, request, cancellationToken).ConfigureAwait(false);
        return FinsCodec.DecodeBitRead(response, count);
    }

    /// <summary>写入位区。</summary>
    public async Task WriteBitsAsync(
        FinsMemoryAreaCode area,
        ushort address,
        byte bitOffset,
        IReadOnlyList<bool> values,
        CancellationToken cancellationToken = default)
    {
        var request = FinsCodec.BuildMemoryAreaWriteRequest(area, address, bitOffset, values);
        var response = await ExecuteAsync(FinsCodec.MemoryAreaWrite, request, cancellationToken).ConfigureAwait(false);
        if (response.Length != 0)
        {
            throw new ZeusProtocolException("FINS 写位响应数据区应为空。请核对 PLC 返回数据。");
        }
    }

    /// <summary>用同一个字填充一段字区。</summary>
    public async Task FillWordsAsync(
        FinsMemoryAreaCode area,
        ushort address,
        ushort count,
        ushort value,
        CancellationToken cancellationToken = default)
    {
        var request = FinsCodec.BuildMemoryAreaFillRequest(area, address, 0, count, value);
        var response = await ExecuteAsync(FinsCodec.MemoryAreaFill, request, cancellationToken).ConfigureAwait(false);
        if (response.Length != 0)
        {
            throw new ZeusProtocolException("FINS 填充响应数据区应为空。请核对 PLC 返回数据。");
        }
    }

    /// <summary>一次读取多个不连续地址。位区返回 <see cref="FinsMemoryValue.BitValue"/>，字区返回 <see cref="FinsMemoryValue.WordValue"/>。</summary>
    public async Task<FinsMemoryValue[]> ReadMultipleAsync(
        IReadOnlyList<FinsMemoryAddress> addresses,
        CancellationToken cancellationToken = default)
    {
        var request = FinsCodec.BuildMultipleMemoryAreaReadRequest(addresses);
        var response = await ExecuteAsync(FinsCodec.MultipleMemoryAreaRead, request, cancellationToken).ConfigureAwait(false);
        return FinsCodec.DecodeMultipleRead(response, addresses);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _channel.DataReceived -= OnDataReceived;
        _channel.StateChanged -= OnStateChanged;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task ExecuteTcpHandshakeAsync(CancellationToken cancellationToken)
    {
        lock (_bufferLock)
        {
            _buffer.Clear();
        }

        await _channel.WriteAsync(FinsCodec.BuildTcpNodeAddressRequest(_options.TcpRequestedClientNode), cancellationToken).ConfigureAwait(false);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            lock (_bufferLock)
            {
                if (FinsCodec.TryDecodeTcpPacket(_buffer, out var command, out var error, out var payload, out var consumed))
                {
                    _buffer.RemoveRange(0, consumed);
                    if (command is not (FinsCodec.TcpCommandNodeAddressDataSend or FinsCodec.TcpCommandNodeAddressDataSendResponse))
                    {
                        throw new ZeusProtocolException($"FINS/TCP 节点地址响应命令异常：0x{command:X8}。");
                    }

                    if (error != 0)
                    {
                        throw new ZeusProtocolException($"FINS/TCP 节点地址握手失败，错误码 0x{error:X8}。");
                    }

                    var (clientNode, serverNode) = FinsCodec.DecodeTcpNodeAddressResponse(payload);
                    _options.SourceNode = clientNode;
                    _options.DestinationNode = serverNode;
                    _tcpHandshakeReady = true;
                    return;
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
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 FINS/TCP 节点地址响应。请检查 PLC IP、端口 9600 与 FINS/TCP 设置。");
            }
        }
    }

    private async Task<FinsResponseFrame> WaitForResponseAsync(ushort command, byte sid, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            lock (_bufferLock)
            {
                if (FinsCodec.TryDecodeResponse(_buffer, _transport, out var response, out var consumed))
                {
                    _buffer.RemoveRange(0, consumed);
                    if (response.ServiceId != sid)
                    {
                        throw new ZeusProtocolException(
                            $"FINS 响应 SID 为 0x{response.ServiceId:X2}，期望 0x{sid:X2}。请避免多路并发共用同一通道。");
                    }

                    if (response.Command != command)
                    {
                        throw new ZeusProtocolException($"FINS 响应命令为 0x{response.Command:X4}，期望 0x{command:X4}。");
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
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 FINS 应答。请检查 PLC 节点号、网络号、端口 9600，或用 FinsSlaveResponder 联调。");
            }
        }
    }

    private byte NextServiceId()
    {
        _serviceId++;
        if (_serviceId == 0)
        {
            _serviceId = 1;
        }

        return _serviceId;
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
                _tcpHandshakeReady = false;
                _buffer.Clear();
                _dataPulse?.TrySetException(new ZeusProtocolException(
                    $"通道 {_channel.Name} 已变为 {e.Current}，未完成的 FINS 请求已取消。"));
                _dataPulse = null;
            }
        }
    }

    private static FinsOptions CopyOptions(FinsOptions source)
        => new()
        {
            DestinationNetwork = source.DestinationNetwork,
            DestinationNode = source.DestinationNode,
            DestinationUnit = source.DestinationUnit,
            SourceNetwork = source.SourceNetwork,
            SourceNode = source.SourceNode,
            SourceUnit = source.SourceUnit,
            GatewayCount = source.GatewayCount,
            InformationControlField = source.InformationControlField,
            TcpRequestedClientNode = source.TcpRequestedClientNode,
            UseTcpNodeAddressHandshake = source.UseTcpNodeAddressHandshake,
            WordOrder = source.WordOrder
        };
}
