namespace Zeus;

/// <summary>
/// 内存通道目录。注册发生在宿主构建期，运行期只读查找。
/// </summary>
public sealed class ChannelRegistry : IChannelRegistry
{
    private readonly Dictionary<string, IChannel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IChannel> _ordered = [];

    /// <inheritdoc />
    public IReadOnlyList<IChannel> All => _ordered;

    /// <summary>
    /// 以唯一名称登记通道。重复名称会立即失败，避免运行期才发现配置冲突。
    /// </summary>
    /// <param name="channel">待登记通道。</param>
    public void Add(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!_channels.TryAdd(channel.Name, channel))
        {
            throw new ZeusException(
                $"通道名称 {channel.Name} 已存在。请为每个串口、套接字或虚拟通道使用不同的名称。");
        }

        _ordered.Add(channel);
    }

    /// <inheritdoc />
    public IChannel Get(string name)
    {
        if (TryGet(name, out var channel) && channel is not null)
        {
            return channel;
        }

        var available = _ordered.Count == 0
            ? "当前尚未注册任何通道"
            : "已注册：" + string.Join("、", _ordered.Select(item => item.Name));
        throw new ZeusException($"找不到名为 {name} 的通道。{available}。");
    }

    /// <inheritdoc />
    public bool TryGet(string name, out IChannel? channel)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            channel = null;
            return false;
        }

        return _channels.TryGetValue(name.Trim(), out channel);
    }
}
