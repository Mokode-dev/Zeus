namespace Zeus;

/// <summary>一个 MQTT 主题点的声明。</summary>
public sealed record MqttPointSpec(
    string Name,
    string Topic,
    MqttDataType DataType,
    PointValueKind Kind,
    PointAlarmLimits? AlarmLimits,
    bool Writable,
    MqttQualityOfService QualityOfService = MqttQualityOfService.AtMostOnce,
    bool Retain = true);
