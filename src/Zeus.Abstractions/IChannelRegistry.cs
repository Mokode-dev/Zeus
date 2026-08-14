namespace Zeus;

/// <summary>
/// 按名称检索并维护通道目录。构建期与运行期都可以增删。
/// </summary>
public interface IChannelRegistry
{
    /// <summary>当前已注册的全部通道快照，顺序与注册顺序一致。</summary>
    IReadOnlyList<IChannel> All { get; }

    /// <summary>通道登记或移除后触发。</summary>
    event EventHandler<ChannelRegistryChangedEventArgs>? Changed;

    /// <summary>
    /// 以唯一名称登记通道。重复名称会立即失败。
    /// 登记本身不打开通道；宿主运行中请使用通信扩展的 <c>Add*Async</c>，它们会在登记后打开。
    /// </summary>
    /// <param name="channel">待登记通道。</param>
    void Add(IChannel channel);

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

    /// <summary>
    /// 移除通道：先从目录摘除，再关闭并释放。
    /// 若仍有设备绑定该通道，调用方应先移除设备，或使用宿主上的级联移除。
    /// </summary>
    /// <param name="name">通道名。</param>
    /// <param name="cancellationToken">取消关闭等待。</param>
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);
}
