namespace Zeus;

/// <summary>
/// 按名称检索已注册通道。宿主启动后即可使用。
/// </summary>
public interface IChannelRegistry
{
    /// <summary>当前已注册的全部通道，顺序与注册顺序一致。</summary>
    IReadOnlyList<IChannel> All { get; }

    /// <summary>
    /// 按名称获取通道。名称比较忽略大小写。
    /// </summary>
    /// <param name="name">注册时使用的通道名。</param>
    /// <returns>对应通道。</returns>
    /// <exception cref="ZeusException">名称不存在时抛出，消息中会列出可用名称以便排错。</exception>
    IChannel Get(string name);

    /// <summary>
    /// 尝试按名称获取通道。
    /// </summary>
    /// <param name="name">注册时使用的通道名。</param>
    /// <param name="channel">找到时为通道实例，否则为 <c>null</c>。</param>
    /// <returns>找到返回 <c>true</c>。</returns>
    bool TryGet(string name, out IChannel? channel);
}
