namespace Zeus;

/// <summary>MQTT 3.1.1 客户端连接选项。</summary>
public sealed class MqttOptions
{
    /// <summary>客户端标识。为空时由设备名生成。</summary>
    public string? ClientId { get; set; }

    /// <summary>可选用户名。</summary>
    public string? Username { get; set; }

    /// <summary>可选密码。只有设置 Username 时才会发送。</summary>
    public string? Password { get; set; }

    /// <summary>保持连接秒数，0 表示禁用保活。</summary>
    public ushort KeepAliveSeconds { get; set; } = 60;

    /// <summary>是否请求清理会话。</summary>
    public bool CleanSession { get; set; } = true;

    /// <summary>可选遗嘱主题。设置后必须同时提供 <see cref="WillPayload"/>。</summary>
    public string? WillTopic { get; set; }

    /// <summary>可选遗嘱载荷。</summary>
    public byte[]? WillPayload { get; set; }

    /// <summary>遗嘱消息服务质量。</summary>
    public MqttQualityOfService WillQualityOfService { get; set; } = MqttQualityOfService.AtMostOnce;

    /// <summary>Broker 是否保留遗嘱消息。</summary>
    public bool WillRetain { get; set; }

    /// <summary>允许接收的最大 MQTT 报文大小，默认 1 MiB。</summary>
    public int MaximumPacketSize { get; set; } = 1024 * 1024;

    /// <summary>是否按 <see cref="KeepAliveSeconds"/> 自动发送 PINGREQ。</summary>
    public bool AutomaticKeepAlive { get; set; } = true;

    /// <summary>通道重新打开后是否自动重连并恢复订阅。</summary>
    public bool AutomaticReconnect { get; set; } = true;

    /// <summary>首次自动重连等待时间。</summary>
    public TimeSpan ReconnectInitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>自动重连等待上限。</summary>
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>自动重连退避倍数。</summary>
    public double ReconnectBackoffMultiplier { get; set; } = 2;
}
