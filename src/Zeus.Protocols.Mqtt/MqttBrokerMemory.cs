namespace Zeus;

internal readonly record struct MqttRetainedMessage(byte[] Payload, MqttQualityOfService QualityOfService);

/// <summary>MQTT 虚拟 Broker 的保留消息内存。</summary>
public sealed class MqttBrokerMemory
{
    private readonly object _sync = new();
    private readonly Dictionary<string, MqttRetainedMessage> _messages = new(StringComparer.Ordinal);

    /// <summary>保存一个主题的保留消息。</summary>
    public void Set(
        string topic,
        ReadOnlySpan<byte> payload,
        MqttQualityOfService qualityOfService = MqttQualityOfService.AtMostOnce)
    {
        MqttCodec.EnsureTopicName(topic);
        MqttCodec.ValidateQualityOfService(qualityOfService);
        lock (_sync)
        {
            if (payload.IsEmpty)
            {
                _messages.Remove(topic.Trim());
            }
            else
            {
                _messages[topic.Trim()] = new MqttRetainedMessage(payload.ToArray(), qualityOfService);
            }
        }
    }

    /// <summary>保存 UTF-8 文本保留消息。</summary>
    public void SetText(
        string topic,
        string payload,
        MqttQualityOfService qualityOfService = MqttQualityOfService.AtMostOnce)
        => Set(topic, System.Text.Encoding.UTF8.GetBytes(payload ?? string.Empty), qualityOfService);

    /// <summary>删除一个主题的保留消息。</summary>
    public bool Remove(string topic)
    {
        MqttCodec.EnsureTopicName(topic);
        lock (_sync)
        {
            return _messages.Remove(topic.Trim());
        }
    }

    /// <summary>读取一个主题的保留消息。</summary>
    public bool TryGet(string topic, out byte[] payload)
    {
        lock (_sync)
        {
            if (_messages.TryGetValue(topic, out var value))
            {
                payload = value.Payload.ToArray();
                return true;
            }
        }

        payload = [];
        return false;
    }

    /// <summary>返回当前保留消息快照。</summary>
    public IReadOnlyDictionary<string, byte[]> Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _messages.ToDictionary(pair => pair.Key, pair => pair.Value.Payload.ToArray(), StringComparer.Ordinal);
            }
        }
    }

    internal IReadOnlyDictionary<string, MqttRetainedMessage> SnapshotMessages
    {
        get
        {
            lock (_sync)
            {
                return _messages.ToDictionary(
                    pair => pair.Key,
                    pair => new MqttRetainedMessage(pair.Value.Payload.ToArray(), pair.Value.QualityOfService),
                    StringComparer.Ordinal);
            }
        }
    }
}
