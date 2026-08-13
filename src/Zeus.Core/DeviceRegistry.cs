namespace Zeus;

/// <summary>
/// 内存设备目录。
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly Dictionary<string, IDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDevice> _ordered = [];

    /// <inheritdoc />
    public IReadOnlyList<IDevice> All => _ordered;

    /// <summary>
    /// 以唯一名称登记设备。
    /// </summary>
    /// <param name="device">待登记设备。</param>
    public void Add(IDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!_devices.TryAdd(device.Name, device))
        {
            throw new ZeusException($"设备名称 {device.Name} 已存在。请为每台设备使用不同的名称。");
        }

        _ordered.Add(device);
    }

    /// <inheritdoc />
    public TDevice Get<TDevice>(string name) where TDevice : class, IDevice
    {
        if (string.IsNullOrWhiteSpace(name) || !_devices.TryGetValue(name.Trim(), out var device))
        {
            var available = _ordered.Count == 0
                ? "当前尚未注册任何设备"
                : "已注册：" + string.Join("、", _ordered.Select(item => item.Name));
            throw new ZeusException($"找不到名为 {name} 的设备。{available}。");
        }

        if (device is TDevice typed)
        {
            return typed;
        }

        throw new ZeusException(
            $"设备 {name} 的实际类型为 {device.GetType().Name}，无法作为 {typeof(TDevice).Name} 使用。请检查 AddDevice 时的泛型参数。");
    }
}
