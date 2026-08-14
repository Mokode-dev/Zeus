namespace Zeus;

/// <summary>
/// 通道目录增删事件参数。
/// </summary>
public sealed class ChannelRegistryChangedEventArgs : EventArgs
{
    /// <summary>
    /// 记录一次目录变更。
    /// </summary>
    /// <param name="change">增或删。</param>
    /// <param name="channel">被操作的通道。移除时实例通常即将关闭。</param>
    public ChannelRegistryChangedEventArgs(ChannelRegistryChange change, IChannel channel)
    {
        Change = change;
        Channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    /// <summary>变更种类。</summary>
    public ChannelRegistryChange Change { get; }

    /// <summary>被操作的通道。</summary>
    public IChannel Channel { get; }
}
