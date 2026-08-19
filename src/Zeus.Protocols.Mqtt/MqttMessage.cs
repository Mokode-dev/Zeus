namespace Zeus;

/// <summary>收到的 MQTT QoS 0 发布消息。</summary>
public sealed class MqttMessage : EventArgs
{
    /// <summary>创建发布消息。</summary>
    public MqttMessage(
        string topic,
        byte[] payload,
        bool retain = false,
        MqttQualityOfService qualityOfService = MqttQualityOfService.AtMostOnce,
        bool duplicate = false,
        ushort? packetIdentifier = null)
    {
        Topic = topic;
        Payload = payload;
        Retain = retain;
        QualityOfService = qualityOfService;
        Duplicate = duplicate;
        PacketIdentifier = packetIdentifier;
    }

    /// <summary>消息主题。</summary>
    public string Topic { get; }

    /// <summary>消息载荷。</summary>
    public byte[] Payload { get; }

    /// <summary>是否为保留消息。</summary>
    public bool Retain { get; }

    /// <summary>消息服务质量。</summary>
    public MqttQualityOfService QualityOfService { get; }

    /// <summary>是否为重复投递。</summary>
    public bool Duplicate { get; }

    /// <summary>QoS 1/2 消息的报文标识符。</summary>
    public ushort? PacketIdentifier { get; }
}
