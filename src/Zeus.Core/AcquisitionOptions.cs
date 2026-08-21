namespace Zeus;

/// <summary>
/// 周期采集选项。未声明任何点时循环不会启动，避免空转占用线程。
/// </summary>
public sealed class AcquisitionOptions
{
    /// <summary>两轮采集之间的间隔，默认 500 毫秒。</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 为 <c>true</c> 时，启动后立刻执行第一轮，而不是先等待一个间隔。
    /// 默认开启，便于界面尽快显示初值。
    /// </summary>
    public bool PollImmediately { get; set; } = true;

    /// <summary>
    /// 单个采集源本轮超时。超过后记点错误并继续其余源。
    /// 小于或等于零表示不在宿主层额外限时，只使用协议自身超时。
    /// </summary>
    public TimeSpan SourceTimeout { get; set; }

    /// <summary>
    /// 为 <c>true</c> 时，同一通道上的设备串行轮询，避免半双工串口或单连接 PLC 并发打架。
    /// 不同通道之间仍并行。默认开启。
    /// </summary>
    public bool SerializePerChannel { get; set; } = true;
}
