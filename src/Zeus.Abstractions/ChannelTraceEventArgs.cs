namespace Zeus;

/// <summary>
/// 通道报文追踪事件参数。
/// </summary>
public sealed class ChannelTraceEventArgs : EventArgs
{
    /// <summary>
    /// 初始化报文追踪事件参数。
    /// </summary>
    /// <param name="direction">报文方向。</param>
    /// <param name="data">本次收发的字节拷贝。</param>
    /// <param name="timestamp">追踪事件产生的时间。</param>
    public ChannelTraceEventArgs(ChannelTraceDirection direction, ReadOnlyMemory<byte> data, DateTimeOffset timestamp)
    {
        Direction = direction;
        Data = data;
        Timestamp = timestamp;
    }

    /// <summary>报文方向。</summary>
    public ChannelTraceDirection Direction { get; }

    /// <summary>本次收发的字节。框架保证其不与底层 IO 缓冲共享。</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>追踪事件产生的 UTC 时间。</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>以连续大写十六进制字符串表示报文，便于写入日志。</summary>
    public string Hex => Convert.ToHexString(Data.Span);
}
