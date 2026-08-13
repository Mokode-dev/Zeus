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
}
