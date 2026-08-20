namespace Zeus;

/// <summary>在一条 Zeus 通道上执行 MQTT 3.1.1 会话。</summary>
public sealed class MqttClient : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly MqttOptions _options;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private readonly Queue<MqttMessage> _messages = [];
    private readonly Dictionary<string, MqttQualityOfService> _subscriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, MqttPublishPacket> _incomingExactlyOnce = [];
    private readonly Queue<byte[]> _pendingControlWrites = [];
    private readonly Queue<MqttMessage> _pendingEvents = [];
    private TaskCompletionSource<bool>? _dataPulse;
    private CancellationTokenSource? _keepAliveCts;
    private CancellationTokenSource? _reconnectCts;
    private Task? _keepAliveTask;
    private Task? _reconnectTask;
    private ushort _nextPacketId;
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private int _connected;
    private int _disposed;
    private bool _everConnected;

    /// <summary>创建客户端并订阅通道事件。</summary>
    public MqttClient(IChannel channel, MqttOptions? options = null, TimeSpan? timeout = null, string? fallbackClientId = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _options = CopyOptions(options ?? new MqttOptions());
        MqttCodec.ValidateOptions(_options);
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "MQTT 超时必须大于 0。\n");
        }

        FallbackClientId = string.IsNullOrWhiteSpace(fallbackClientId) ? "zeus-client" : fallbackClientId.Trim();
        MqttCodec.EnsureUtf8(FallbackClientId, "fallbackClientId");
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>连接时使用的备用客户端标识。</summary>
    public string FallbackClientId { get; }

    /// <summary>当前是否已收到 CONNACK。</summary>
    public bool IsConnected => Volatile.Read(ref _connected) != 0;

    /// <summary>当前会话选项副本。</summary>
    public MqttOptions Options => CopyOptions(_options);

    /// <summary>收到发布消息时触发。</summary>
    public event EventHandler<MqttMessage>? MessageReceived;

    /// <summary>发送 CONNECT 并等待 CONNACK。</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ConnectLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>订阅一个 QoS 0 主题过滤器。</summary>
    public Task SubscribeAsync(string topicFilter, CancellationToken cancellationToken = default)
        => SubscribeAsync(topicFilter, MqttQualityOfService.AtMostOnce, cancellationToken);

    /// <summary>订阅一个主题过滤器并协商最大 QoS。</summary>
    public async Task SubscribeAsync(
        string topicFilter,
        MqttQualityOfService qualityOfService,
        CancellationToken cancellationToken = default)
    {
        MqttCodec.EnsureTopicFilter(topicFilter);
        MqttCodec.ValidateQualityOfService(qualityOfService);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureConnectedLockedAsync(cancellationToken).ConfigureAwait(false);
            var packetId = NextPacketId();
            TouchActivity();
            await _channel.WriteAsync(MqttCodec.EncodeSubscribe(packetId, topicFilter, qualityOfService), cancellationToken).ConfigureAwait(false);
            var packet = await WaitForPacketAsync(MqttPacketType.SubAck, "SUBSCRIBE", cancellationToken).ConfigureAwait(false);
            if (MqttCodec.ReadPacketId(packet, "SUBACK") != packetId)
            {
                throw new MqttException($"MQTT SUBACK 报文标识符不匹配，期望 {packetId}。");
            }

            var granted = MqttCodec.ReadReturnCode(packet, "SUBACK");
            if (granted == 0x80)
            {
                throw new MqttException($"MQTT Broker 未接受主题订阅 {topicFilter}。");
            }

            if (granted > (byte)MqttQualityOfService.ExactlyOnce)
            {
                throw new MqttException($"MQTT SUBACK 返回未知 QoS 0x{granted:X2}。");
            }

            _subscriptions[topicFilter.Trim()] = (MqttQualityOfService)granted;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>取消订阅一个主题过滤器。</summary>
    public async Task UnsubscribeAsync(string topicFilter, CancellationToken cancellationToken = default)
    {
        MqttCodec.EnsureTopicFilter(topicFilter);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureConnectedLockedAsync(cancellationToken).ConfigureAwait(false);
            var packetId = NextPacketId();
            TouchActivity();
            await _channel.WriteAsync(MqttCodec.EncodeUnsubscribe(packetId, topicFilter), cancellationToken).ConfigureAwait(false);
            var packet = await WaitForPacketAsync(MqttPacketType.UnsubAck, "UNSUBSCRIBE", cancellationToken).ConfigureAwait(false);
            if (MqttCodec.ReadPacketId(packet, "UNSUBACK") != packetId)
            {
                throw new MqttException($"MQTT UNSUBACK 报文标识符不匹配，期望 {packetId}。");
            }

            _subscriptions.Remove(topicFilter.Trim());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>发布消息。默认使用 QoS 0。</summary>
    public Task PublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        bool retain = false,
        CancellationToken cancellationToken = default)
        => PublishAsync(topic, payload, MqttQualityOfService.AtMostOnce, retain, cancellationToken);

    /// <summary>按指定 QoS 发布消息并等待必要的确认。</summary>
    public async Task PublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        MqttQualityOfService qualityOfService,
        bool retain = false,
        CancellationToken cancellationToken = default)
    {
        MqttCodec.EnsureTopicName(topic);
        MqttCodec.ValidateQualityOfService(qualityOfService);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureConnectedLockedAsync(cancellationToken).ConfigureAwait(false);
            ushort? packetId = qualityOfService == MqttQualityOfService.AtMostOnce ? null : NextPacketId();
            TouchActivity();
            await _channel.WriteAsync(MqttCodec.EncodePublish(topic, payload.Span, retain, qualityOfService, packetId), cancellationToken).ConfigureAwait(false);
            if (packetId is { } identifier)
            {
                if (qualityOfService == MqttQualityOfService.AtLeastOnce)
                {
                    await WaitForAcknowledgementAsync(MqttPacketType.PubAck, identifier, "PUBLISH QoS 1", cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WaitForAcknowledgementAsync(MqttPacketType.PubRec, identifier, "PUBLISH QoS 2", cancellationToken).ConfigureAwait(false);
                    TouchActivity();
                    await _channel.WriteAsync(MqttCodec.EncodeAcknowledgement(MqttPacketType.PubRel, identifier), cancellationToken).ConfigureAwait(false);
                    await WaitForAcknowledgementAsync(MqttPacketType.PubComp, identifier, "PUBREL", cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>解析当前已完整收到的发布消息并返回快照。</summary>
    public IReadOnlyList<MqttMessage> DrainMessages()
    {
        List<byte[]> controlWrites;
        List<MqttMessage> events;
        IReadOnlyList<MqttMessage> result;
        lock (_bufferLock)
        {
            DrainPublishPacketsLocked();
            result = _messages.ToArray();
            _messages.Clear();
            controlWrites = DequeueControlWritesLocked();
            events = DequeueEventsLocked();
        }

        FlushControlAndEvents(controlWrites, events);
        return result;
    }

    /// <summary>发送 PINGREQ 并等待 PINGRESP。</summary>
    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureConnectedLockedAsync(cancellationToken).ConfigureAwait(false);
            TouchActivity();
            await _channel.WriteAsync(MqttCodec.EncodePingReq(), cancellationToken).ConfigureAwait(false);
            await WaitForPacketAsync(MqttPacketType.PingResp, "PINGREQ", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>等待下一条符合主题过滤器的消息。</summary>
    public async Task<MqttMessage> WaitForMessageAsync(string? topicFilter = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (topicFilter is not null)
        {
            MqttCodec.EnsureTopicFilter(topicFilter);
        }

        while (true)
        {
            TaskCompletionSource<bool>? pulse = null;
            List<byte[]> controlWrites;
            List<MqttMessage> events;
            MqttMessage? matched = null;
            lock (_bufferLock)
            {
                DrainPublishPacketsLocked();
                while (_messages.Count > 0)
                {
                    var message = _messages.Dequeue();
                    if (topicFilter is null || MqttCodec.TopicMatches(topicFilter, message.Topic))
                    {
                        matched = message;
                        break;
                    }
                }

                if (matched is null)
                {
                    pulse = _dataPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                controlWrites = DequeueControlWritesLocked();
                events = DequeueEventsLocked();
            }

            FlushControlAndEvents(controlWrites, events);
            if (matched is { } found)
            {
                return found;
            }

            if (pulse is null)
            {
                continue;
            }

            await pulse.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>发送 DISCONNECT。通道仍由宿主管理。</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                TouchActivity();
                await _channel.WriteAsync(MqttCodec.EncodeDisconnect(), cancellationToken).ConfigureAwait(false);
                SetDisconnected(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopBackgroundTasks();
        _channel.DataReceived -= OnDataReceived;
        _channel.StateChanged -= OnStateChanged;
        var reconnect = _reconnectTask;
        if (reconnect is not null)
        {
            try { await reconnect.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        var keepAlive = _keepAliveTask;
        if (keepAlive is not null)
        {
            try { await keepAlive.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        _gate.Dispose();
        _keepAliveCts?.Dispose();
        _reconnectCts?.Dispose();
    }

    private async Task ConnectLockedAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        ClearBuffer();
        TouchActivity();
        await _channel.WriteAsync(MqttCodec.EncodeConnect(_options, FallbackClientId), cancellationToken).ConfigureAwait(false);
        var packet = await WaitForPacketAsync(MqttPacketType.ConnAck, "CONNECT", cancellationToken).ConfigureAwait(false);
        if (MqttCodec.ReadReturnCode(packet, "CONNACK") != 0)
        {
            throw new MqttException($"MQTT Broker 拒绝连接，返回码 0x{packet.Body[^1]:X2}。");
        }

        SetConnected();
        _everConnected = true;
        StartKeepAlive();
    }

    private async Task EnsureConnectedLockedAsync(CancellationToken cancellationToken)
    {
        await ConnectLockedAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<MqttPacket> WaitForPacketAsync(MqttPacketType expected, string operation, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);
        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            TaskCompletionSource<bool>? pulse = null;
            List<byte[]> controlWrites;
            List<MqttMessage> events;
            MqttPacket? matched = null;
            lock (_bufferLock)
            {
                if (MqttCodec.TryDecodePacket(_buffer, out var packet, out var consumed, _options.MaximumPacketSize))
                {
                    _buffer.RemoveRange(0, consumed);
                    if (!RouteIncomingPacketLocked(packet))
                    {
                        if (packet.Type != expected)
                        {
                            throw new MqttException($"MQTT {operation} 收到 {packet.Type}，期望 {expected}。");
                        }

                        TouchActivity();
                        matched = packet;
                    }
                }
                else
                {
                    pulse = _dataPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                controlWrites = DequeueControlWritesLocked();
                events = DequeueEventsLocked();
            }

            FlushControlAndEvents(controlWrites, events);
            if (matched is { } found)
            {
                return found;
            }

            if (pulse is null)
            {
                continue;
            }

            try
            {
                await pulse.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ZeusProtocolException($"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 MQTT {operation} 应答。");
            }
        }
    }

    private async Task WaitForAcknowledgementAsync(MqttPacketType expected, ushort packetIdentifier, string operation, CancellationToken cancellationToken)
    {
        while (true)
        {
            var packet = await WaitForPacketAsync(expected, operation, cancellationToken).ConfigureAwait(false);
            if (MqttCodec.ReadPacketId(packet, operation) == packetIdentifier)
            {
                return;
            }

            throw new MqttException($"MQTT {operation} 确认报文标识符不匹配，期望 {packetIdentifier}。");
        }
    }

    private bool RouteIncomingPacketLocked(MqttPacket packet)
    {
        switch (packet.Type)
        {
            case MqttPacketType.Publish:
                var publish = MqttCodec.DecodePublish(packet);
                if (publish.QualityOfService == MqttQualityOfService.AtLeastOnce)
                {
                    _pendingControlWrites.Enqueue(MqttCodec.EncodeAcknowledgement(MqttPacketType.PubAck, publish.PacketIdentifier!.Value));
                    EnqueuePublish(publish);
                }
                else if (publish.QualityOfService == MqttQualityOfService.ExactlyOnce)
                {
                    _incomingExactlyOnce[publish.PacketIdentifier!.Value] = publish;
                    _pendingControlWrites.Enqueue(MqttCodec.EncodeAcknowledgement(MqttPacketType.PubRec, publish.PacketIdentifier.Value));
                }
                else
                {
                    EnqueuePublish(publish);
                }

                return true;
            case MqttPacketType.PubRel:
                var releaseIdentifier = MqttCodec.ReadPacketId(packet, "PUBREL");
                if (_incomingExactlyOnce.Remove(releaseIdentifier, out var pending))
                {
                    EnqueuePublish(pending);
                }

                _pendingControlWrites.Enqueue(MqttCodec.EncodeAcknowledgement(MqttPacketType.PubComp, releaseIdentifier));
                return true;
            case MqttPacketType.PingReq:
                _pendingControlWrites.Enqueue(MqttCodec.EncodePingResp());
                return true;
            case MqttPacketType.PubAck when packet.Type == MqttPacketType.PubAck:
            case MqttPacketType.PubRec when packet.Type == MqttPacketType.PubRec:
            case MqttPacketType.PubComp when packet.Type == MqttPacketType.PubComp:
                return false;
            default:
                return false;
        }
    }

    private void EnqueuePublish(MqttPublishPacket publish)
    {
        var message = new MqttMessage(
            publish.Topic,
            publish.Payload,
            publish.Retain,
            publish.QualityOfService,
            publish.Duplicate,
            publish.PacketIdentifier);
        _messages.Enqueue(message);
        _pendingEvents.Enqueue(message);
    }

    private void DrainPublishPacketsLocked()
    {
        while (MqttCodec.TryDecodePacket(_buffer, out var packet, out var consumed, _options.MaximumPacketSize))
        {
            if (packet.Type is not (MqttPacketType.Publish or MqttPacketType.PubRel or MqttPacketType.PingReq))
            {
                break;
            }

            _buffer.RemoveRange(0, consumed);
            RouteIncomingPacketLocked(packet);
        }
    }

    private void FlushControlAndEvents(List<byte[]> controlWrites, List<MqttMessage> events)
    {
        foreach (var packet in controlWrites)
        {
            TouchActivity();
            _ = WriteControlAsync(packet);
        }

        var handler = MessageReceived;
        if (handler is null)
        {
            return;
        }

        foreach (var message in events)
        {
            handler(this, message);
        }
    }

    private List<byte[]> DequeueControlWritesLocked()
    {
        if (_pendingControlWrites.Count == 0)
        {
            return [];
        }

        var result = _pendingControlWrites.ToArray().ToList();
        _pendingControlWrites.Clear();
        return result;
    }

    private List<MqttMessage> DequeueEventsLocked()
    {
        if (_pendingEvents.Count == 0)
        {
            return [];
        }

        var result = _pendingEvents.ToArray().ToList();
        _pendingEvents.Clear();
        return result;
    }

    private async Task WriteControlAsync(byte[] packet)
    {
        try
        {
            TouchActivity();
            await _channel.WriteAsync(packet).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 通道状态变化会唤醒正在等待的操作；控制确认不能从事件线程向外抛出。
        }
    }

    private void OnDataReceived(object? sender, ChannelDataReceivedEventArgs e)
    {
        List<byte[]> controlWrites;
        List<MqttMessage> events;
        lock (_bufferLock)
        {
            if (!ProtocolReceiveBuffer.TryAppend(_buffer, e.Data.Span, _options.MaximumPacketSize))
            {
                _dataPulse?.TrySetException(ProtocolReceiveBuffer.Overflow(_channel.Name, _options.MaximumPacketSize));
                _dataPulse = null;
                controlWrites = DequeueControlWritesLocked();
                events = DequeueEventsLocked();
            }
            else
            {
                TouchActivity();
                DrainUnsolicitedPacketsLocked();
                _dataPulse?.TrySetResult(true);
                _dataPulse = null;
                controlWrites = DequeueControlWritesLocked();
                events = DequeueEventsLocked();
            }
        }

        FlushControlAndEvents(controlWrites, events);
    }

    private void DrainUnsolicitedPacketsLocked()
    {
        while (MqttCodec.TryDecodePacket(_buffer, out var packet, out var consumed, _options.MaximumPacketSize))
        {
            if (packet.Type is not (MqttPacketType.Publish or MqttPacketType.PubRel or MqttPacketType.PingReq))
            {
                return;
            }

            _buffer.RemoveRange(0, consumed);
            RouteIncomingPacketLocked(packet);
        }
    }

    private void OnStateChanged(object? sender, ChannelStateChangedEventArgs e)
    {
        if (e.Current is ChannelState.Closed or ChannelState.Faulted)
        {
            SetDisconnected(_everConnected && _options.AutomaticReconnect);
            lock (_bufferLock)
            {
                _buffer.Clear();
                _dataPulse?.TrySetException(new ZeusProtocolException($"通道 {_channel.Name} 已变为 {e.Current}，未完成的 MQTT 请求已取消。"));
                _dataPulse = null;
            }

            return;
        }

        if (e.Current == ChannelState.Open && _everConnected && _options.AutomaticReconnect)
        {
            StartReconnect();
        }
    }

    private void StartKeepAlive()
    {
        StopKeepAlive();
        if (!_options.AutomaticKeepAlive || _options.KeepAliveSeconds == 0)
        {
            return;
        }

        _keepAliveCts = new CancellationTokenSource();
        _keepAliveTask = KeepAliveLoopAsync(_keepAliveCts.Token);
    }

    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.KeepAliveSeconds / 2d));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            if (!IsConnected || DateTime.UtcNow - _lastActivityUtc < TimeSpan.FromSeconds(_options.KeepAliveSeconds))
            {
                continue;
            }

            try
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (IsConnected)
                    {
                        TouchActivity();
                        await _channel.WriteAsync(MqttCodec.EncodePingReq(), cancellationToken).ConfigureAwait(false);
                        await WaitForPacketAsync(MqttPacketType.PingResp, "自动保活", cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception)
            {
                SetDisconnected(_options.AutomaticReconnect);
            }
        }
    }

    private void StartReconnect()
    {
        if (_reconnectTask is { IsCompleted: false } || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var previous = _reconnectCts;
        previous?.Cancel();
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;
        previous?.Dispose();
        _reconnectTask = ReconnectLoopAsync(token);
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        var delay = _options.ReconnectInitialDelay;
        while (!cancellationToken.IsCancellationRequested && !IsConnected)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await ConnectLockedAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var subscription in _subscriptions.ToArray())
                    {
                        var packetId = NextPacketId();
                        TouchActivity();
                        await _channel.WriteAsync(MqttCodec.EncodeSubscribe(packetId, subscription.Key, subscription.Value), cancellationToken).ConfigureAwait(false);
                        var packet = await WaitForPacketAsync(MqttPacketType.SubAck, "重连订阅", cancellationToken).ConfigureAwait(false);
                        if (MqttCodec.ReadPacketId(packet, "重连 SUBACK") != packetId || MqttCodec.ReadReturnCode(packet, "重连 SUBACK") >= 0x80)
                        {
                            throw new MqttException($"MQTT 重连后恢复订阅失败：{subscription.Key}。");
                        }
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception)
            {
                SetDisconnected(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(_options.ReconnectMaxDelay.TotalMilliseconds, Math.Max(1, delay.TotalMilliseconds * _options.ReconnectBackoffMultiplier)));
            }
        }
    }

    private void SetConnected() => Volatile.Write(ref _connected, 1);

    private void SetDisconnected(bool startReconnect)
    {
        Volatile.Write(ref _connected, 0);
        StopKeepAlive();
        if (startReconnect)
        {
            StartReconnect();
        }
    }

    private void StopKeepAlive()
    {
        _keepAliveCts?.Cancel();
    }

    private void StopBackgroundTasks()
    {
        _keepAliveCts?.Cancel();
        _reconnectCts?.Cancel();
    }

    private void ClearBuffer()
    {
        lock (_bufferLock)
        {
            _buffer.Clear();
        }
    }

    private void TouchActivity() => _lastActivityUtc = DateTime.UtcNow;

    private ushort NextPacketId()
    {
        _nextPacketId = (ushort)(_nextPacketId == ushort.MaxValue ? 1 : _nextPacketId + 1);
        return _nextPacketId;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(MqttClient));
        }
    }

    private static MqttOptions CopyOptions(MqttOptions source)
        => new()
        {
            ClientId = source.ClientId,
            Username = source.Username,
            Password = source.Password,
            KeepAliveSeconds = source.KeepAliveSeconds,
            CleanSession = source.CleanSession,
            WillTopic = source.WillTopic,
            WillPayload = source.WillPayload?.ToArray(),
            WillQualityOfService = source.WillQualityOfService,
            WillRetain = source.WillRetain,
            MaximumPacketSize = source.MaximumPacketSize,
            AutomaticKeepAlive = source.AutomaticKeepAlive,
            AutomaticReconnect = source.AutomaticReconnect,
            ReconnectInitialDelay = source.ReconnectInitialDelay,
            ReconnectMaxDelay = source.ReconnectMaxDelay,
            ReconnectBackoffMultiplier = source.ReconnectBackoffMultiplier
        };
}
