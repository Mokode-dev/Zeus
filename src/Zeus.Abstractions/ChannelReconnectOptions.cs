namespace Zeus;

/// <summary>
/// 通道故障后的自动重连选项。
/// 仅对进入 <see cref="ChannelState.Faulted"/> 的通道生效；主动关闭不会重连。
/// </summary>
public sealed class ChannelReconnectOptions
{
    /// <summary>
    /// 是否在通道故障后自动再次 <see cref="IChannel.OpenAsync"/>。
    /// 默认开启；现场若要自行控制重连，设为 <c>false</c>。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>首次重连等待时间，默认 1 秒。</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>指数退避的上限，默认 30 秒。</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 连续失败时把等待时间乘以该系数。必须大于或等于 1，默认 2。
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2;
}
