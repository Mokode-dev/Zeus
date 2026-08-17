namespace Zeus;

/// <summary>
/// 在一条 TCP 通道上执行 EtherNet/IP 封装层与 CIP 请求。同一客户端串行发送。
/// </summary>
public sealed class EtherNetIpClient : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly EtherNetIpOptions _options;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private TaskCompletionSource<bool>? _dataPulse;
    private uint _sessionHandle;
    private ulong _senderContext;

    /// <summary>创建 EtherNet/IP 客户端。通道通常是 TCP 客户端，端口为 44818。</summary>
    public EtherNetIpClient(IChannel channel, EtherNetIpOptions? options = null, TimeSpan? timeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _options = CopyOptions(options ?? new EtherNetIpOptions());
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>当前会话句柄；尚未 Register Session 时为 0。</summary>
    public uint SessionHandle => _sessionHandle;

    /// <summary>会话选项。</summary>
    public EtherNetIpOptions Options => CopyOptions(_options);

    /// <summary>执行任意 CIP 服务，返回已去掉 CIP 状态头的数据区。</summary>
    public async Task<byte[]> ExecuteCipAsync(byte service, ReadOnlyMemory<byte> path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            var context = NextSenderContext();
            var cip = EtherNetIpCodec.EncodeCipRequest(service, path.Span, data.Span);
            var request = EtherNetIpCodec.EncodeSendRRData(_sessionHandle, _options.CpfTimeout, context, cip);
            await _channel.WriteAsync(request, cancellationToken).ConfigureAwait(false);

            var packet = await WaitForPacketAsync(EtherNetIpCodec.SendRRData, context, "EtherNet/IP SendRRData", cancellationToken).ConfigureAwait(false);
            var cipResponse = EtherNetIpCodec.DecodeSendRRData(packet.Data);
            return EtherNetIpCodec.DecodeCipResponse(service, cipResponse);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>读取 CIP 对象单个属性。</summary>
    public Task<byte[]> GetAttributeSingleAsync(ushort classId, uint instanceId, ushort attributeId, CancellationToken cancellationToken = default)
        => ExecuteCipAsync(EtherNetIpCodec.ServiceGetAttributeSingle, EtherNetIpCodec.BuildAttributePath(classId, instanceId, attributeId), ReadOnlyMemory<byte>.Empty, cancellationToken);

    /// <summary>写入 CIP 对象单个属性。</summary>
    public Task SetAttributeSingleAsync(ushort classId, uint instanceId, ushort attributeId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => ExecuteCipAsync(EtherNetIpCodec.ServiceSetAttributeSingle, EtherNetIpCodec.BuildAttributePath(classId, instanceId, attributeId), data, cancellationToken);

    /// <summary>读取 Allen-Bradley 符号标签。</summary>
    public async Task<object> ReadTagAsync(string tagName, EtherNetIpDataType dataType, ushort elementCount = 1, double? scale = null, CancellationToken cancellationToken = default)
    {
        var request = EtherNetIpCodec.BuildReadTagRequest(tagName, elementCount);
        var response = await ExecuteRawCipRequestAsync(EtherNetIpCodec.ServiceReadTag, request, cancellationToken).ConfigureAwait(false);
        return EtherNetIpCodec.DecodeTagReadResponse(response, dataType, scale, elementCount);
    }

    /// <summary>写入 Allen-Bradley 符号标签。</summary>
    public async Task WriteTagAsync(string tagName, EtherNetIpDataType dataType, object value, double? scale = null, CancellationToken cancellationToken = default)
    {
        var request = EtherNetIpCodec.BuildWriteTagRequest(tagName, dataType, value, scale);
        _ = await ExecuteRawCipRequestAsync(EtherNetIpCodec.ServiceWriteTag, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _channel.DataReceived -= OnDataReceived;
        _channel.StateChanged -= OnStateChanged;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<byte[]> ExecuteRawCipRequestAsync(byte expectedService, byte[] request, CancellationToken cancellationToken)
    {
        if (request.Length < 2 || request[0] != expectedService)
        {
            throw new ZeusProtocolException("EtherNet/IP 内部请求服务码不匹配。");
        }

        var pathLength = request[1] * 2;
        return await ExecuteCipAsync(expectedService, request.AsMemory(2, pathLength), request.AsMemory(2 + pathLength), cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessionHandle != 0)
        {
            return;
        }

        lock (_bufferLock)
        {
            _buffer.Clear();
        }

        var context = NextSenderContext();
        await _channel.WriteAsync(EtherNetIpCodec.EncodeRegisterSession(_options.ProtocolVersion, context), cancellationToken).ConfigureAwait(false);
        var packet = await WaitForPacketAsync(EtherNetIpCodec.RegisterSession, context, "EtherNet/IP Register Session", cancellationToken).ConfigureAwait(false);
        _sessionHandle = EtherNetIpCodec.DecodeRegisterSession(packet.Data, packet);
        if (_sessionHandle == 0)
        {
            throw new ZeusProtocolException("EtherNet/IP Register Session 未返回有效会话句柄。");
        }
    }

    private async Task<EtherNetIpPacket> WaitForPacketAsync(ushort command, ulong senderContext, string operation, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            lock (_bufferLock)
            {
                while (EtherNetIpCodec.TryDecodePacket(_buffer, out var packet, out var consumed))
                {
                    _buffer.RemoveRange(0, consumed);
                    if (packet.Command != command || packet.SenderContext != senderContext)
                    {
                        continue;
                    }

                    if (packet.Status != 0)
                    {
                        throw new EtherNetIpException($"{operation} 返回封装层状态 0x{packet.Status:X8}。", packet.Status);
                    }

                    return packet;
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
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 {operation} 应答。请检查 PLC IP、端口 44818，或用 EtherNetIpSlaveResponder 联调。");
            }
        }
    }

    private ulong NextSenderContext()
    {
        _senderContext++;
        if (_senderContext == 0)
        {
            _senderContext = 1;
        }

        return _senderContext;
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
                _sessionHandle = 0;
                _buffer.Clear();
                _dataPulse?.TrySetException(new ZeusProtocolException(
                    $"通道 {_channel.Name} 已变为 {e.Current}，未完成的 EtherNet/IP 请求已取消。"));
                _dataPulse = null;
            }
        }
    }

    private static EtherNetIpOptions CopyOptions(EtherNetIpOptions source)
        => new()
        {
            ProtocolVersion = source.ProtocolVersion,
            CpfTimeout = source.CpfTimeout
        };
}
