namespace Zeus;

/// <summary>
/// 在一条通道上执行 IEC 60870-5-104 请求。同一客户端串行发送 APDU 并校验确认。
/// </summary>
public sealed class Iec104Client : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly Iec104Options _options;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private TaskCompletionSource<bool>? _dataPulse;
    private ushort _sendSequence;
    private ushort _receiveSequence;
    private bool _started;

    /// <summary>创建 IEC104 客户端并订阅通道。</summary>
    public Iec104Client(IChannel channel, Iec104Options? options = null, TimeSpan? timeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _options = CopyOptions(options ?? new Iec104Options());
        Iec104Codec.ValidateOptions(_options);
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>当前会话选项副本。</summary>
    public Iec104Options Options => CopyOptions(_options);

    /// <summary>发送 STARTDT act 并等待 STARTDT con。后续读写会自动确保已启动。</summary>
    public async Task StartDataTransferAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStartedLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>执行总召唤，返回收到的内置信息对象。</summary>
    public async Task<IReadOnlyList<Iec104InformationObject>> InterrogateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStartedLockedAsync(cancellationToken).ConfigureAwait(false);
            ClearBuffer();
            await _channel.WriteAsync(
                Iec104Codec.EncodeInterrogationCommand(_options, NextSendSequence(), _receiveSequence),
                cancellationToken).ConfigureAwait(false);

            var values = new List<Iec104InformationObject>();
            var confirmed = false;
            while (true)
            {
                var apdu = await WaitForApduAsync("总召唤响应", cancellationToken).ConfigureAwait(false);
                if (HandleSupervisoryFrame(apdu))
                {
                    continue;
                }

                var header = Iec104Codec.DecodeAsduHeader(apdu.Asdu);
                if (header.CommonAddress != _options.CommonAddress)
                {
                    continue;
                }

                if (header.TypeId == Iec104Codec.TypeInterrogationCommand)
                {
                    if (header.Cause == Iec104CauseOfTransmission.ActivationConfirmation)
                    {
                        confirmed = true;
                        continue;
                    }

                    if (header.Cause == Iec104CauseOfTransmission.ActivationTermination)
                    {
                        return values;
                    }

                    throw new Iec104Exception(header.TypeId, header.Cause);
                }

                if (confirmed)
                {
                    values.AddRange(Iec104Codec.DecodeInformationObjects(apdu.Asdu));
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>发送单点命令并等待激活确认。</summary>
    public Task SendSingleCommandAsync(int address, bool command, CancellationToken cancellationToken = default)
        => SendCommandAsync(Iec104DataType.SinglePoint, address, command, cancellationToken);

    /// <summary>发送归一化设点命令并等待激活确认。线值范围为 -1 到 1。</summary>
    public Task SendNormalizedSetpointAsync(int address, double value, CancellationToken cancellationToken = default)
        => SendCommandAsync(Iec104DataType.Normalized, address, value, cancellationToken);

    /// <summary>发送标度化设点命令并等待激活确认。</summary>
    public Task SendScaledSetpointAsync(int address, short value, CancellationToken cancellationToken = default)
        => SendCommandAsync(Iec104DataType.Scaled, address, value, cancellationToken);

    /// <summary>发送短浮点设点命令并等待激活确认。</summary>
    public Task SendShortFloatSetpointAsync(int address, double value, CancellationToken cancellationToken = default)
        => SendCommandAsync(Iec104DataType.ShortFloat, address, value, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _channel.DataReceived -= OnDataReceived;
        _channel.StateChanged -= OnStateChanged;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task SendCommandAsync(Iec104DataType dataType, int address, object value, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStartedLockedAsync(cancellationToken).ConfigureAwait(false);
            ClearBuffer();
            var request = dataType == Iec104DataType.SinglePoint
                ? Iec104Codec.EncodeSingleCommand(_options, address, Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture), NextSendSequence(), _receiveSequence)
                : Iec104Codec.EncodeSetpoint(_options, address, dataType, value, NextSendSequence(), _receiveSequence);
            await _channel.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            await WaitForCommandConfirmationAsync(Iec104Codec.ToCommandType(dataType), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStartedLockedAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        ClearBuffer();
        await _channel.WriteAsync(Iec104Codec.EncodeStartDataTransferActivation(), cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var apdu = await WaitForApduAsync("STARTDT 确认", cancellationToken).ConfigureAwait(false);
            if (Iec104Codec.IsStartDataTransferConfirmation(apdu))
            {
                _started = true;
                return;
            }
        }
    }

    private async Task WaitForCommandConfirmationAsync(byte expectedTypeId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var apdu = await WaitForApduAsync("命令激活确认", cancellationToken).ConfigureAwait(false);
            if (HandleSupervisoryFrame(apdu))
            {
                continue;
            }

            var header = Iec104Codec.DecodeAsduHeader(apdu.Asdu);
            if (header.TypeId != expectedTypeId)
            {
                continue;
            }

            if (header.Cause == Iec104CauseOfTransmission.ActivationConfirmation)
            {
                return;
            }

            throw new Iec104Exception(header.TypeId, header.Cause);
        }
    }

    private bool HandleSupervisoryFrame(Iec104Apdu apdu)
    {
        if (apdu.Format == Iec104FrameFormat.I)
        {
            _receiveSequence = NextSequence(apdu.SendSequence);
            return false;
        }

        if (Iec104Codec.IsTestFrameActivation(apdu))
        {
            _ = _channel.WriteAsync(Iec104Codec.EncodeTestFrameConfirmation());
        }

        return true;
    }

    private async Task<Iec104Apdu> WaitForApduAsync(string operation, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            lock (_bufferLock)
            {
                if (Iec104Codec.TryDecodeApdu(_buffer, out var apdu, out var consumed))
                {
                    _buffer.RemoveRange(0, consumed);
                    return apdu;
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
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 IEC104 {operation}。请检查 TCP 连接、公共地址或用 Iec104SlaveResponder 联调。");
            }
        }
    }

    private void ClearBuffer()
    {
        lock (_bufferLock)
        {
            _buffer.Clear();
        }
    }

    private ushort NextSendSequence()
    {
        var current = _sendSequence;
        _sendSequence = NextSequence(_sendSequence);
        return current;
    }

    private static ushort NextSequence(ushort value) => (ushort)((value + 1) & 0x7FFF);

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
                _started = false;
                _dataPulse?.TrySetException(new ZeusProtocolException(
                    $"通道 {_channel.Name} 已变为 {e.Current}，未完成的 IEC104 请求已取消。"));
                _dataPulse = null;
            }
        }
    }

    private static Iec104Options CopyOptions(Iec104Options source)
        => new()
        {
            CommonAddress = source.CommonAddress,
            OriginatorAddress = source.OriginatorAddress,
            InterrogationQualifier = source.InterrogationQualifier
        };
}
