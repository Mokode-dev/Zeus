namespace Zeus;

/// <summary>MQTT 对端返回了连接、订阅或报文错误。</summary>
public sealed class MqttException : ZeusProtocolException
{
    /// <summary>创建 MQTT 异常。</summary>
    public MqttException(string message)
        : base(message)
    {
    }
}
