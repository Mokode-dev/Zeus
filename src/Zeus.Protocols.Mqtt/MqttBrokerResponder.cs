namespace Zeus;

/// <summary>用于虚拟通道联调的 MQTT 3.1.1 Broker。</summary>
public sealed class MqttBrokerResponder : IVirtualResponder
{
    private readonly MqttBrokerMemory _memory;
    private readonly Dictionary<string, MqttQualityOfService> _subscriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, MqttPublishPacket> _incomingExactlyOnce = [];
    private bool _connected;
    private ushort _nextPacketIdentifier;

    /// <summary>创建虚拟 Broker。</summary>
    public MqttBrokerResponder(MqttBrokerMemory? memory = null)
    {
        _memory = memory ?? new MqttBrokerMemory();
    }

    /// <summary>虚拟 Broker 使用的保留消息内存。</summary>
    public MqttBrokerMemory Memory => _memory;

    /// <summary>最近一次 CONNECT 中的客户端标识。</summary>
    public string? ClientId { get; private set; }

    /// <summary>最近一次 CONNECT 中的遗嘱消息。</summary>
    public MqttMessage? WillMessage { get; private set; }

    /// <summary>收到的 CONNECT 次数。</summary>
    public int ConnectCount { get; private set; }

    /// <summary>收到的 PINGREQ 次数。</summary>
    public int PingRequestCount { get; private set; }

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Respond(ReadOnlyMemory<byte> request)
    {
        var buffer = request.ToArray().ToList();
        var output = new List<byte>();
        while (MqttCodec.TryDecodePacket(buffer, out var packet, out var consumed))
        {
            buffer.RemoveRange(0, consumed);
            switch (packet.Type)
            {
                case MqttPacketType.Connect:
                    var connect = MqttCodec.DecodeConnect(packet);
                    ClientId = connect.ClientId;
                    WillMessage = connect.WillTopic is null
                        ? null
                        : new MqttMessage(
                            connect.WillTopic,
                            connect.WillPayload!,
                            connect.WillRetain,
                            connect.WillQualityOfService);
                    ConnectCount++;
                    if (connect.CleanSession)
                    {
                        _subscriptions.Clear();
                        _incomingExactlyOnce.Clear();
                    }

                    _connected = true;
                    output.AddRange(MqttCodec.EncodeConnAck(sessionPresent: !connect.CleanSession && _subscriptions.Count > 0));
                    break;
                case MqttPacketType.Subscribe:
                    EnsureConnected();
                    var subscription = MqttCodec.DecodeSubscription(packet, out var subscribePacketId);
                    _subscriptions[subscription.TopicFilter] = subscription.QualityOfService;
                    output.AddRange(MqttCodec.EncodeSubAck(subscribePacketId, subscription.QualityOfService));
                    AppendRetainedMessages(output, subscription);
                    break;
                case MqttPacketType.Unsubscribe:
                    EnsureConnected();
                    var filter = MqttCodec.DecodeUnsubscribe(packet, out var unsubscribePacketId);
                    _subscriptions.Remove(filter);
                    output.AddRange(MqttCodec.EncodeAcknowledgement(MqttPacketType.UnsubAck, unsubscribePacketId));
                    break;
                case MqttPacketType.Publish:
                    EnsureConnected();
                    HandlePublish(output, MqttCodec.DecodePublish(packet));
                    break;
                case MqttPacketType.PubRel:
                    EnsureConnected();
                    var releasePacketId = MqttCodec.ReadPacketId(packet, "PUBREL");
                    if (_incomingExactlyOnce.Remove(releasePacketId, out var pending))
                    {
                        CommitPublish(output, pending);
                    }

                    output.AddRange(MqttCodec.EncodeAcknowledgement(MqttPacketType.PubComp, releasePacketId));
                    break;
                case MqttPacketType.PubRec:
                    EnsureConnected();
                    output.AddRange(MqttCodec.EncodeAcknowledgement(
                        MqttPacketType.PubRel,
                        MqttCodec.ReadPacketId(packet, "PUBREC")));
                    break;
                case MqttPacketType.PubAck:
                case MqttPacketType.PubComp:
                    EnsureConnected();
                    break;
                case MqttPacketType.PingReq:
                    EnsureConnected();
                    PingRequestCount++;
                    output.AddRange(MqttCodec.EncodePingResp());
                    break;
                case MqttPacketType.Disconnect:
                    _connected = false;
                    break;
                default:
                    throw new MqttException($"虚拟 MQTT Broker 不支持 {packet.Type} 报文。");
            }
        }

        if (buffer.Count != 0)
        {
            throw new MqttException("虚拟 MQTT Broker 收到不完整报文；一次虚拟通道写入应包含完整 MQTT 报文。");
        }

        return output.Count == 0 ? null : output.ToArray();
    }

    private void HandlePublish(List<byte> output, MqttPublishPacket publish)
    {
        switch (publish.QualityOfService)
        {
            case MqttQualityOfService.AtMostOnce:
                CommitPublish(output, publish);
                break;
            case MqttQualityOfService.AtLeastOnce:
                CommitPublish(output, publish);
                output.AddRange(MqttCodec.EncodeAcknowledgement(MqttPacketType.PubAck, publish.PacketIdentifier!.Value));
                break;
            case MqttQualityOfService.ExactlyOnce:
                _incomingExactlyOnce[publish.PacketIdentifier!.Value] = publish;
                output.AddRange(MqttCodec.EncodeAcknowledgement(MqttPacketType.PubRec, publish.PacketIdentifier.Value));
                break;
        }
    }

    private void CommitPublish(List<byte> output, MqttPublishPacket publish)
    {
        if (publish.Retain)
        {
            _memory.Set(publish.Topic, publish.Payload, publish.QualityOfService);
        }

        var granted = MatchingQualityOfService(publish.Topic);
        if (granted is null)
        {
            return;
        }

        var deliveryQuality = (MqttQualityOfService)Math.Min((int)publish.QualityOfService, (int)granted.Value);
        ushort? packetIdentifier = deliveryQuality == MqttQualityOfService.AtMostOnce ? null : NextPacketIdentifier();
        output.AddRange(MqttCodec.EncodePublish(
            publish.Topic,
            publish.Payload,
            retain: false,
            deliveryQuality,
            packetIdentifier));
    }

    private void AppendRetainedMessages(List<byte> output, MqttSubscription subscription)
    {
        foreach (var pair in _memory.SnapshotMessages)
        {
            if (!MqttCodec.TopicMatches(subscription.TopicFilter, pair.Key))
            {
                continue;
            }

            var deliveryQuality = (MqttQualityOfService)Math.Min(
                (int)pair.Value.QualityOfService,
                (int)subscription.QualityOfService);
            ushort? packetIdentifier = deliveryQuality == MqttQualityOfService.AtMostOnce ? null : NextPacketIdentifier();
            output.AddRange(MqttCodec.EncodePublish(
                pair.Key,
                pair.Value.Payload,
                retain: true,
                deliveryQuality,
                packetIdentifier));
        }
    }

    private MqttQualityOfService? MatchingQualityOfService(string topic)
    {
        MqttQualityOfService? result = null;
        foreach (var subscription in _subscriptions)
        {
            if (MqttCodec.TopicMatches(subscription.Key, topic)
                && (result is null || subscription.Value > result.Value))
            {
                result = subscription.Value;
            }
        }

        return result;
    }

    private ushort NextPacketIdentifier()
    {
        _nextPacketIdentifier = (ushort)(_nextPacketIdentifier == ushort.MaxValue ? 1 : _nextPacketIdentifier + 1);
        return _nextPacketIdentifier;
    }

    private void EnsureConnected()
    {
        if (!_connected)
        {
            throw new MqttException("MQTT 客户端尚未 CONNECT。");
        }
    }
}
