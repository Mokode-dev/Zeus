namespace Zeus;

/// <summary>
/// 已记录的一条通道报文。
/// </summary>
public sealed class ChannelTraceEntry
{
    /// <summary>
    /// 创建通道报文记录。
    /// </summary>
    /// <param name="channelName">通道名。</param>
    /// <param name="direction">报文方向。</param>
    /// <param name="data">报文字节。</param>
    /// <param name="timestamp">记录时间。</param>
    public ChannelTraceEntry(
        string channelName,
        ChannelTraceDirection direction,
        ReadOnlyMemory<byte> data,
        DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            throw new ArgumentException("通道名不能为空。", nameof(channelName));
        }

        ChannelName = channelName.Trim();
        Direction = direction;
        Data = data.ToArray();
        Timestamp = timestamp;
    }

    /// <summary>产生报文的通道名。</summary>
    public string ChannelName { get; }

    /// <summary>报文方向。</summary>
    public ChannelTraceDirection Direction { get; }

    /// <summary>报文字节的独立拷贝。</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>记录时间。</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>以连续大写十六进制字符串表示报文。</summary>
    public string Hex => Convert.ToHexString(Data.Span);
}
