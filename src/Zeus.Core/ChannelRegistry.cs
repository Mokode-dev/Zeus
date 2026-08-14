namespace Zeus;

/// <summary>
/// 内存通道目录。构建期与运行期都可以增删；查找与快照在锁内完成。
/// </summary>
public sealed class ChannelRegistry : IChannelRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IChannel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IChannel> _ordered = [];

    /// <inheritdoc />
    public IReadOnlyList<IChannel> All
    {
        get
        {
            lock (_gate)
            {
                return _ordered.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<ChannelRegistryChangedEventArgs>? Changed;

    /// <inheritdoc />
    public void Add(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        lock (_gate)
        {
            if (!_channels.TryAdd(channel.Name, channel))
            {
                throw new ZeusException(
                    $"通道名称 {channel.Name} 已存在。请为每个串口、套接字或虚拟通道使用不同的名称。");
            }

            _ordered.Add(channel);
        }

        Changed?.Invoke(this, new ChannelRegistryChangedEventArgs(ChannelRegistryChange.Added, channel));
    }

    /// <inheritdoc />
    public IChannel Get(string name)
    {
        if (TryGet(name, out var channel) && channel is not null)
        {
            return channel;
        }

        lock (_gate)
        {
            var available = _ordered.Count == 0
                ? "当前尚未注册任何通道"
                : "已注册：" + string.Join("、", _ordered.Select(item => item.Name));
            throw new ZeusException($"找不到名为 {name} 的通道。{available}。");
        }
    }

    /// <inheritdoc />
    public bool TryGet(string name, out IChannel? channel)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            channel = null;
            return false;
        }

        lock (_gate)
        {
            return _channels.TryGetValue(name.Trim(), out channel);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!TryGet(name, out var channel) || channel is null)
        {
            throw new ZeusException($"找不到名为 {name} 的通道，无法移除。");
        }

        lock (_gate)
        {
            _channels.Remove(channel.Name);
            _ordered.Remove(channel);
        }

        Changed?.Invoke(this, new ChannelRegistryChangedEventArgs(ChannelRegistryChange.Removed, channel));

        try
        {
            await channel.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
    }
}
