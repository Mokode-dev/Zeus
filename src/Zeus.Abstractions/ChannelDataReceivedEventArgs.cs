namespace Zeus;

/// <summary>
/// 通道收到原始字节时的事件参数。
/// 载荷已从底层接收缓冲复制，订阅方可安全缓存或跨线程使用。
/// </summary>
public sealed class ChannelDataReceivedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化接收事件参数。
    /// </summary>
    /// <param name="data">本次收到的完整拷贝。</param>
    public ChannelDataReceivedEventArgs(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }

    /// <summary>本次收到的字节。框架保证其不与内部接收缓冲共享。</summary>
    public ReadOnlyMemory<byte> Data { get; }
}
