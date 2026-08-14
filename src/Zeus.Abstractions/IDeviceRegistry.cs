namespace Zeus;

/// <summary>
/// 按名称检索并维护设备目录。构建期与运行期都可以增删。
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>当前已注册的全部设备快照。</summary>
    IReadOnlyList<IDevice> All { get; }

    /// <summary>设备登记或移除后触发。</summary>
    event EventHandler<DeviceRegistryChangedEventArgs>? Changed;

    /// <summary>
    /// 以唯一名称登记设备。重复名称会立即失败。
    /// </summary>
    /// <param name="device">待登记设备。</param>
    void Add(IDevice device);

    /// <summary>
    /// 按名称获取指定类型的设备。
    /// </summary>
    /// <typeparam name="TDevice">期望的设备类型。</typeparam>
    /// <param name="name">注册时使用的设备名。</param>
    /// <returns>类型匹配的设备实例。</returns>
    TDevice Get<TDevice>(string name) where TDevice : class, IDevice;

    /// <summary>
    /// 尝试按名称获取指定类型的设备。
    /// </summary>
    /// <typeparam name="TDevice">期望的设备类型。</typeparam>
    /// <param name="name">设备名。</param>
    /// <param name="device">找到且类型匹配时为实例，否则为 <c>null</c>。</param>
    /// <returns>找到且类型匹配返回 <c>true</c>。</returns>
    bool TryGet<TDevice>(string name, out TDevice? device) where TDevice : class, IDevice;

    /// <summary>
    /// 移除设备并从点表摘除其贡献的点。通道保持不动。
    /// </summary>
    /// <param name="name">设备名。</param>
    /// <param name="cancellationToken">取消释放等待。</param>
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);
}
