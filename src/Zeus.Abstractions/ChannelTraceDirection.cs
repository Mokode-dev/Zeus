namespace Zeus;

/// <summary>
/// 通道报文方向。
/// </summary>
public enum ChannelTraceDirection
{
    /// <summary>应用写入到底层传输的字节。</summary>
    Sent = 0,

    /// <summary>底层传输收到并发布给应用的字节。</summary>
    Received = 1
}
