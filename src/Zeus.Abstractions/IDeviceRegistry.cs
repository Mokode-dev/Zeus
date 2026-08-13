namespace Zeus;

/// <summary>
/// 按名称检索已注册设备。
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>当前已注册的全部设备。</summary>
    IReadOnlyList<IDevice> All { get; }

    /// <summary>
    /// 按名称获取指定类型的设备。
    /// </summary>
    /// <typeparam name="TDevice">期望的设备类型。</typeparam>
    /// <param name="name">注册时使用的设备名。</param>
    /// <returns>类型匹配的设备实例。</returns>
    TDevice Get<TDevice>(string name) where TDevice : class, IDevice;
}
