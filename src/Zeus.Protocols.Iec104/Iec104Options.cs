namespace Zeus;

/// <summary>
/// IEC 60870-5-104 会话选项，包含应用层公共地址与链路层 t1/t2/t3、k/w 窗口。
/// 定时器默认值对齐 IEC 60870-5-104 附录：t1=15s、t2=10s、t3=20s、k=12、w=8。
/// </summary>
public sealed class Iec104Options
{
    /// <summary>公共地址，默认 1。</summary>
    public int CommonAddress { get; set; } = 1;

    /// <summary>传送原因中的源发地址，默认 0。</summary>
    public int OriginatorAddress { get; set; }

    /// <summary>总召唤限定词 QOI，默认 20，表示站总召唤。</summary>
    public int InterrogationQualifier { get; set; } = 20;

    /// <summary>
    /// t1：发送 I 格式或 U 格式（STARTDT/TESTFR act）后等待确认的超时。超时将复位链路。
    /// 默认 15 秒。设为 <see cref="TimeSpan.Zero"/> 可关闭（仅联调）。
    /// </summary>
    public TimeSpan T1 { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// t2：收到 I 格式后最迟发送 S 格式确认的等待。必须小于 t1。
    /// 默认 10 秒。设为 <see cref="TimeSpan.Zero"/> 时仅按 w 窗口确认。
    /// </summary>
    public TimeSpan T2 { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// t3：链路空闲后发送 TESTFR act 的间隔。默认 20 秒。
    /// 设为 <see cref="TimeSpan.Zero"/> 可关闭自动保活。
    /// </summary>
    public TimeSpan T3 { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// k：未确认 I 格式的最大数量。达到后暂停发送 I 格式，直到对端用 N(R) 确认。
    /// 默认 12。
    /// </summary>
    public int MaxUnacknowledgedIFrames { get; set; } = 12;

    /// <summary>
    /// w：最迟在收到这么多 I 格式后必须发送确认，不必等 t2。默认 8，且必须小于 k。
    /// </summary>
    public int AcknowledgeWindow { get; set; } = 8;
}
