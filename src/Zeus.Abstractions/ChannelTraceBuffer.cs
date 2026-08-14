namespace Zeus;

/// <summary>
/// 通道报文滚动记录器。适合调试窗口、故障快照或测试中保留最近 N 条收发记录。
/// </summary>
public sealed class ChannelTraceBuffer : IDisposable
{
    private readonly object _sync = new();
    private readonly IChannel _channel;
    private readonly Queue<ChannelTraceEntry> _entries = new();
    private bool _disposed;

    /// <summary>
    /// 订阅一个通道并保留最近的报文记录。
    /// </summary>
    /// <param name="channel">要追踪的通道。</param>
    /// <param name="capacity">最多保留的记录数。</param>
    public ChannelTraceBuffer(IChannel channel, int capacity = 256)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "报文记录容量必须大于 0。");
        }

        _channel = channel;
        Capacity = capacity;
        _channel.PacketTraced += OnPacketTraced;
    }

    /// <summary>最多保留的记录数。</summary>
    public int Capacity { get; }

    /// <summary>当前快照，顺序从旧到新。</summary>
    public IReadOnlyList<ChannelTraceEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>清空当前已记录的报文。</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.PacketTraced -= OnPacketTraced;
    }

    private void OnPacketTraced(object? sender, ChannelTraceEventArgs e)
    {
        var entry = new ChannelTraceEntry(_channel.Name, e.Direction, e.Data, e.Timestamp);
        lock (_sync)
        {
            while (_entries.Count >= Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
        }
    }
}
