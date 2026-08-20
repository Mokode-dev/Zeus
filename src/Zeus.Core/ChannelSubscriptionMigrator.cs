namespace Zeus;

/// <summary>
/// 热重载重建通道时，把旧实例上残留的事件订阅迁到同名新实例。
/// 设备在重建前会先释放并退订，因此迁过去的通常是界面或业务代码的订阅。
/// </summary>
internal sealed class ChannelSubscriptionMigrator
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CapturedSubscriptions> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 订阅通道目录变更。
    /// </summary>
    public ChannelSubscriptionMigrator(IChannelRegistry channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        channels.Changed += OnChanged;
    }

    private void OnChanged(object? sender, ChannelRegistryChangedEventArgs e)
    {
        if (e.Channel is not ChannelBase channel)
        {
            return;
        }

        if (e.Change == ChannelRegistryChange.Removed)
        {
            var captured = channel.CaptureSubscriptions();
            if (captured.IsEmpty)
            {
                return;
            }

            lock (_gate)
            {
                _pending[channel.Name] = captured;
            }

            return;
        }

        CapturedSubscriptions pending;
        lock (_gate)
        {
            if (!_pending.Remove(channel.Name, out pending))
            {
                return;
            }
        }

        channel.RestoreSubscriptions(pending);
    }
}

/// <summary>
/// 从 <see cref="ChannelBase"/> 拷出的多播委托快照。
/// </summary>
internal readonly struct CapturedSubscriptions
{
    public CapturedSubscriptions(
        EventHandler<ChannelStateChangedEventArgs>? stateChanged,
        EventHandler<ChannelDataReceivedEventArgs>? dataReceived,
        EventHandler<ChannelTraceEventArgs>? packetTraced)
    {
        StateChanged = stateChanged;
        DataReceived = dataReceived;
        PacketTraced = packetTraced;
    }

    public EventHandler<ChannelStateChangedEventArgs>? StateChanged { get; }

    public EventHandler<ChannelDataReceivedEventArgs>? DataReceived { get; }

    public EventHandler<ChannelTraceEventArgs>? PacketTraced { get; }

    public bool IsEmpty => StateChanged is null && DataReceived is null && PacketTraced is null;
}
