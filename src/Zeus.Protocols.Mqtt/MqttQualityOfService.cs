namespace Zeus;

/// <summary>MQTT 消息服务质量。</summary>
public enum MqttQualityOfService
{
    /// <summary>最多一次，不确认也不重传。</summary>
    AtMostOnce = 0,

    /// <summary>至少一次，使用 PUBACK 确认。</summary>
    AtLeastOnce = 1,

    /// <summary>恰好一次，使用 PUBREC/PUBREL/PUBCOMP 四步握手。</summary>
    ExactlyOnce = 2
}
