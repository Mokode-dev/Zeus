namespace Zeus;

/// <summary>MQTT 主题点的常用载荷类型。</summary>
public enum MqttDataType
{
    /// <summary>UTF-8 文本。</summary>
    Text = 1,

    /// <summary>布尔文本，支持 true/false 和 1/0。</summary>
    Boolean = 2,

    /// <summary>不带小数的 32 位有符号整数。</summary>
    Int32 = 3,

    /// <summary>不带小数的 64 位有符号整数。</summary>
    Int64 = 4,

    /// <summary>UTF-8 文本形式的双精度浮点数。</summary>
    Double = 5,

    /// <summary>原始字节。</summary>
    Bytes = 6
}
