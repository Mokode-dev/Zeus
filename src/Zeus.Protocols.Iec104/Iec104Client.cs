namespace Zeus;

/// <summary>
/// 在一条通道上执行 IEC 60870-5-104 请求。
/// 应用层串行发送 ASDU；链路层按 t1/t2/t3 与 k/w 窗口维护 I/S/U 确认。
/// </summary>
public sealed class Iec104Client : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly Iec104Options _options;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private readonly Queue<UnackedIFrame> _unackedOutgoing = [];
    private TaskCompletionSource<bool>? _dataPulse;
    private TaskCompletionSource<bool>? _windowPulse;
    private CancellationTokenSource? _linkCts;
    private Task? _linkTask;
    private ushort _sendSequence;
    private ushort _receiveSequence;
    private int _unackedIncoming;
    private DateTime _lastIncomingIUtc;
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private DateTime? _outstandingUSentUtc;
    private byte _outstandingUControl;
    private bool _started;
    private int _disposed;

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

    /// <summary>是否已完成 STARTDT 且链路未因 t1 超时复位。</summary>
    public bool IsDataTransferStarted => _started;

    /// <summary>尚未被对端 N(R) 确认的 I 格式数量。</summary>
    public int UnacknowledgedOutgoingIFrames
    {
        get
        {
            lock (_bufferLock)
            {
                return _unackedOutgoing.Count;
            }
        }
    }

    /// <summary>已收到但尚未用 S 格式确认的 I 格式数量。</summary>
    public int UnacknowledgedIncomingIFrames => Volatile.Read(ref _unackedIncoming);

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
            var interrogationNs = NextSendSequence();
            await SendIFrameLockedAsync(
                interrogationNs,
                Iec104Codec.EncodeInterrogationCommand(_options, interrogationNs, _receiveSequence),
                cancellationToken).ConfigureAwait(false);

            var values = new List<Iec104InformationObject>();
            while (true)
            {
                var apdu = await WaitForApduAsync("总召唤响应", cancellationToken).ConfigureAwait(false);
                if (await HandleLinkFrameAsync(apdu, cancellationToken).ConfigureAwait(false))
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
                        continue;
                    }

                    if (header.Cause == Iec104CauseOfTransmission.ActivationTermination)
                    {
                        await MaybeAcknowledgeLockedAsync(force: true, cancellationToken).ConfigureAwait(false);
                        return values;
                    }

                    throw new Iec104Exception(header.TypeId, header.Cause);
                }

                values.AddRange(Iec104Codec.DecodeInformationObjects(apdu.Asdu));
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
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.DataReceived -= OnDataReceived;
        _channel.StateChanged -= OnStateChanged;
        var link = StopLink();
        if (link is not null)
        {
            try
            {
                await link.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _linkCts?.Dispose();
        _gate.Dispose();
    }

    private async Task SendCommandAsync(Iec104DataType dataType, int address, object value, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStartedLockedAsync(cancellationToken).ConfigureAwait(false);
            var nS = NextSendSequence();
            var request = dataType == Iec104DataType.SinglePoint
                ? Iec104Codec.EncodeSingleCommand(_options, address, Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture), nS, _receiveSequence)
                : Iec104Codec.EncodeSetpoint(_options, address, dataType, value, nS, _receiveSequence);
            await SendIFrameLockedAsync(nS, request, cancellationToken).ConfigureAwait(false);
            await WaitForCommandConfirmationAsync(Iec104Codec.ToCommandType(dataType), cancellationToken).ConfigureAwait(false);
            await MaybeAcknowledgeLockedAsync(force: true, cancellationToken).ConfigureAwait(false);
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

        await SendUFrameLockedAsync(Iec104Codec.EncodeStartDataTransferActivation(), 0x07, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var apdu = await WaitForApduAsync("STARTDT 确认", cancellationToken).ConfigureAwait(false);
            if (Iec104Codec.IsStartDataTransferConfirmation(apdu))
            {
                ClearOutstandingU();
                _started = true;
                TouchActivity();
                StartLink();
                return;
            }

            await HandleLinkFrameAsync(apdu, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForCommandConfirmationAsync(byte expectedTypeId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var apdu = await WaitForApduAsync("命令激活确认", cancellationToken).ConfigureAwait(false);
            if (await HandleLinkFrameAsync(apdu, cancellationToken).ConfigureAwait(false))
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

    /// <summary>
    /// 处理链路层帧。返回 <c>true</c> 表示本帧不是待处理的 I 格式 ASDU。
    /// </summary>
    private async Task<bool> HandleLinkFrameAsync(Iec104Apdu apdu, CancellationToken cancellationToken)
    {
        TouchActivity();
        if (apdu.Format == Iec104FrameFormat.I)
        {
            AcceptIncomingI(apdu);
            AcknowledgeOutgoing(apdu.ReceiveSequence);
            await MaybeAcknowledgeLockedAsync(force: false, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (apdu.Format == Iec104FrameFormat.S)
        {
            AcknowledgeOutgoing(apdu.ReceiveSequence);
            return true;
        }

        if (Iec104Codec.IsTestFrameActivation(apdu))
        {
            await _channel.WriteAsync(Iec104Codec.EncodeTestFrameConfirmation(), cancellationToken).ConfigureAwait(false);
            TouchActivity();
            return true;
        }

        if (Iec104Codec.IsTestFrameConfirmation(apdu))
        {
            if (_outstandingUControl == 0x43)
            {
                ClearOutstandingU();
            }

            return true;
        }

        if (Iec104Codec.IsStartDataTransferConfirmation(apdu))
        {
            ClearOutstandingU();
            _started = true;
            StartLink();
            return true;
        }

        return true;
    }

    private void AcceptIncomingI(Iec104Apdu apdu)
    {
        if (apdu.SendSequence != _receiveSequence)
        {
            throw new Iec104Exception(
                $"IEC104 收到乱序 I 格式：N(S)={apdu.SendSequence}，期望 {_receiveSequence}。链路将复位。");
        }

        _receiveSequence = NextSequence(apdu.SendSequence);
        _unackedIncoming++;
        _lastIncomingIUtc = DateTime.UtcNow;
        Volatile.Write(ref _unackedIncoming, _unackedIncoming);
    }

    private void AcknowledgeOutgoing(ushort receiveSequence)
    {
        lock (_bufferLock)
        {
            while (_unackedOutgoing.Count > 0 && SequenceLessThan(_unackedOutgoing.Peek().SendSequence, receiveSequence))
            {
                _unackedOutgoing.Dequeue();
            }

            _windowPulse?.TrySetResult(true);
            _windowPulse = null;
        }
    }

    private async Task SendIFrameLockedAsync(ushort nS, byte[] frame, CancellationToken cancellationToken)
    {
        await WaitForSendWindowAsync(cancellationToken).ConfigureAwait(false);
        lock (_bufferLock)
        {
            _unackedOutgoing.Enqueue(new UnackedIFrame(nS, DateTime.UtcNow));
        }

        await _channel.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        TouchActivity();
        _unackedIncoming = 0;
        Volatile.Write(ref _unackedIncoming, 0);
    }

    private async Task SendUFrameLockedAsync(byte[] frame, byte control, CancellationToken cancellationToken)
    {
        _outstandingUControl = control;
        _outstandingUSentUtc = DateTime.UtcNow;
        await _channel.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        TouchActivity();
    }

    private async Task WaitForSendWindowAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TaskCompletionSource<bool>? pulse;
            lock (_bufferLock)
            {
                if (_unackedOutgoing.Count < _options.MaxUnacknowledgedIFrames)
                {
                    return;
                }

                pulse = _windowPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_options.T1 > TimeSpan.Zero)
            {
                timeoutCts.CancelAfter(_options.T1);
            }

            try
            {
                await pulse.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new Iec104Exception($"IEC104 t1 超时：k 窗口 {_options.MaxUnacknowledgedIFrames} 内未收到 N(R) 确认。");
            }
        }
    }

    private async Task MaybeAcknowledgeLockedAsync(bool force, CancellationToken cancellationToken)
    {
        if (_unackedIncoming <= 0)
        {
            return;
        }

        var dueByWindow = _unackedIncoming >= _options.AcknowledgeWindow;
        var dueByTimer = _options.T2 > TimeSpan.Zero
            && DateTime.UtcNow - _lastIncomingIUtc >= _options.T2;
        if (!force && !dueByWindow && !dueByTimer)
        {
            return;
        }

        await _channel.WriteAsync(Iec104Codec.EncodeSupervisory(_receiveSequence), cancellationToken).ConfigureAwait(false);
        _unackedIncoming = 0;
        Volatile.Write(ref _unackedIncoming, 0);
        TouchActivity();
    }

    private async Task<Iec104Apdu> WaitForApduAsync(string operation, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            await ServiceTimersLockedAsync(cancellationToken).ConfigureAwait(false);

            Iec104Apdu? decoded = null;
            lock (_bufferLock)
            {
                if (Iec104Codec.TryDecodeApdu(_buffer, out var apdu, out var consumed))
                {
                    _buffer.RemoveRange(0, consumed);
                    decoded = apdu;
                }
                else if (consumed > 0)
                {
                    _buffer.RemoveRange(0, Math.Min(consumed, _buffer.Count));
                }
                else
                {
                    _dataPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            if (decoded is { } found)
            {
                return found;
            }

            if (_dataPulse is null)
            {
                continue;
            }

            try
            {
                var slice = NextLinkServiceDelay();
                using var sliceCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
                if (slice < TimeSpan.MaxValue && slice > TimeSpan.Zero)
                {
                    sliceCts.CancelAfter(slice);
                }

                await _dataPulse.Task.WaitAsync(sliceCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new ZeusProtocolException(
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 IEC104 {operation}。请检查 TCP 连接、公共地址或用 Iec104SlaveResponder 联调。");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 切片到期：回到循环顶部执行 t1/t2/t3 巡检。
            }
        }
    }

    private ushort NextSendSequence()
    {
        var current = _sendSequence;
        _sendSequence = NextSequence(_sendSequence);
        return current;
    }

    private static ushort NextSequence(ushort value) => (ushort)((value + 1) & 0x7FFF);

    /// <summary>15 位序号：a 在模 32768 意义下小于 b。</summary>
    private static bool SequenceLessThan(ushort a, ushort b)
    {
        var distance = (ushort)((b - a) & 0x7FFF);
        return distance != 0 && distance < 0x4000;
    }

    private void TouchActivity() => _lastActivityUtc = DateTime.UtcNow;

    /// <summary>
    /// 计算下一次链路巡检的等待上限，使 t1/t2/t3 能在业务等待期间到期。
    /// </summary>
    private TimeSpan NextLinkServiceDelay()
    {
        var now = DateTime.UtcNow;
        var delay = TimeSpan.FromMilliseconds(50);
        if (_options.T1 > TimeSpan.Zero)
        {
            if (_outstandingUSentUtc is { } uSent)
            {
                delay = MinPositive(delay, _options.T1 - (now - uSent));
            }

            DateTime? oldest = null;
            lock (_bufferLock)
            {
                if (_unackedOutgoing.Count > 0)
                {
                    oldest = _unackedOutgoing.Peek().SentUtc;
                }
            }

            if (oldest is { } sent)
            {
                delay = MinPositive(delay, _options.T1 - (now - sent));
            }
        }

        if (_options.T2 > TimeSpan.Zero && _unackedIncoming > 0)
        {
            delay = MinPositive(delay, _options.T2 - (now - _lastIncomingIUtc));
        }

        if (_started && _options.T3 > TimeSpan.Zero && _outstandingUSentUtc is null)
        {
            delay = MinPositive(delay, _options.T3 - (now - _lastActivityUtc));
        }

        return delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay;
    }

    private static TimeSpan MinPositive(TimeSpan left, TimeSpan right)
        => right <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : (right < left ? right : left);

    private void ClearOutstandingU()
    {
        _outstandingUControl = 0;
        _outstandingUSentUtc = null;
    }

    private void StartLink()
    {
        if (_linkTask is { IsCompleted: false })
        {
            return;
        }

        _linkCts?.Cancel();
        _linkCts?.Dispose();
        _linkCts = new CancellationTokenSource();
        _linkTask = LinkLoopAsync(_linkCts.Token);
    }

    private Task? StopLink()
    {
        _linkCts?.Cancel();
        return _linkTask;
    }

    private async Task LinkLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                try
                {
                    if (_started)
                    {
                        await DrainUnsolicitedLinkFramesAsync(cancellationToken).ConfigureAwait(false);
                        await ServiceTimersLockedAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Iec104Exception)
            {
                ResetLink();
            }
            catch (Exception)
            {
                // 链路空闲巡检失败不打断业务；下一次请求或通道故障会表面化。
            }
        }
    }

    private async Task ServiceTimersLockedAsync(CancellationToken cancellationToken)
    {
        ThrowIfT1Elapsed();
        await MaybeAcknowledgeLockedAsync(force: false, cancellationToken).ConfigureAwait(false);
        await MaybeSendTestFrameLockedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 空闲时消化缓冲中的 S/U 格式，避免 TESTFR con 积压导致 t1 误超时。
    /// I 格式留给正在进行的总召唤/命令等待，以免抢走业务 ASDU。
    /// </summary>
    private async Task DrainUnsolicitedLinkFramesAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Iec104Apdu apdu;
            lock (_bufferLock)
            {
                if (!Iec104Codec.TryDecodeApdu(_buffer, out apdu, out var consumed))
                {
                    if (consumed > 0)
                    {
                        _buffer.RemoveRange(0, Math.Min(consumed, _buffer.Count));
                        continue;
                    }

                    return;
                }

                if (apdu.Format == Iec104FrameFormat.I)
                {
                    return;
                }

                _buffer.RemoveRange(0, consumed);
            }

            await HandleLinkFrameAsync(apdu, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ThrowIfT1Elapsed()
    {
        if (_options.T1 <= TimeSpan.Zero)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_outstandingUSentUtc is { } uSent && now - uSent >= _options.T1)
        {
            throw new Iec104Exception("IEC104 t1 超时：U 格式（STARTDT/TESTFR act）未在 t1 内得到确认。");
        }

        UnackedIFrame oldest;
        lock (_bufferLock)
        {
            if (_unackedOutgoing.Count == 0)
            {
                return;
            }

            oldest = _unackedOutgoing.Peek();
        }

        if (now - oldest.SentUtc >= _options.T1)
        {
            throw new Iec104Exception("IEC104 t1 超时：I 格式未在 t1 内得到 N(R) 确认。");
        }
    }

    private async Task MaybeSendTestFrameLockedAsync(CancellationToken cancellationToken)
    {
        if (!_started || _options.T3 <= TimeSpan.Zero || _outstandingUSentUtc is not null)
        {
            return;
        }

        if (DateTime.UtcNow - _lastActivityUtc < _options.T3)
        {
            return;
        }

        await SendUFrameLockedAsync(Iec104Codec.EncodeTestFrameActivation(), 0x43, cancellationToken).ConfigureAwait(false);
    }

    private void ResetLink()
    {
        _started = false;
        _sendSequence = 0;
        _receiveSequence = 0;
        _unackedIncoming = 0;
        Volatile.Write(ref _unackedIncoming, 0);
        ClearOutstandingU();
        lock (_bufferLock)
        {
            _unackedOutgoing.Clear();
            _buffer.Clear();
            _windowPulse?.TrySetException(new Iec104Exception("IEC104 链路已复位。"));
            _windowPulse = null;
            _dataPulse?.TrySetException(new Iec104Exception("IEC104 链路已复位。"));
            _dataPulse = null;
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
            StopLink();
            ResetLink();
            lock (_bufferLock)
            {
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
            InterrogationQualifier = source.InterrogationQualifier,
            T1 = source.T1,
            T2 = source.T2,
            T3 = source.T3,
            MaxUnacknowledgedIFrames = source.MaxUnacknowledgedIFrames,
            AcknowledgeWindow = source.AcknowledgeWindow
        };

    private readonly record struct UnackedIFrame(ushort SendSequence, DateTime SentUtc);
}
