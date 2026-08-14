namespace Zeus;

/// <summary>
/// 内存设备目录。构建期与运行期都可以增删。
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDevice> _ordered = [];

    /// <inheritdoc />
    public IReadOnlyList<IDevice> All
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
    public event EventHandler<DeviceRegistryChangedEventArgs>? Changed;

    /// <inheritdoc />
    public void Add(IDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_gate)
        {
            if (!_devices.TryAdd(device.Name, device))
            {
                throw new ZeusException($"设备名称 {device.Name} 已存在。请为每台设备使用不同的名称。");
            }

            _ordered.Add(device);
        }

        Changed?.Invoke(this, new DeviceRegistryChangedEventArgs(DeviceRegistryChange.Added, device));
    }

    /// <inheritdoc />
    public TDevice Get<TDevice>(string name) where TDevice : class, IDevice
    {
        if (TryGet<TDevice>(name, out var typed) && typed is not null)
        {
            return typed;
        }

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(name) || !_devices.TryGetValue(name.Trim(), out var device))
            {
                var available = _ordered.Count == 0
                    ? "当前尚未注册任何设备"
                    : "已注册：" + string.Join("、", _ordered.Select(item => item.Name));
                throw new ZeusException($"找不到名为 {name} 的设备。{available}。");
            }

            throw new ZeusException(
                $"设备 {name} 的实际类型为 {device.GetType().Name}，无法作为 {typeof(TDevice).Name} 使用。请检查 AddDevice 时的泛型参数。");
        }
    }

    /// <inheritdoc />
    public bool TryGet<TDevice>(string name, out TDevice? device) where TDevice : class, IDevice
    {
        device = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_devices.TryGetValue(name.Trim(), out var existing) || existing is not TDevice typed)
            {
                return false;
            }

            device = typed;
            return true;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        IDevice? device;
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(name) || !_devices.TryGetValue(name.Trim(), out device))
            {
                throw new ZeusException($"找不到名为 {name} 的设备，无法移除。");
            }

            _devices.Remove(device.Name);
            _ordered.Remove(device);
        }

        Changed?.Invoke(this, new DeviceRegistryChangedEventArgs(DeviceRegistryChange.Removed, device));

        if (device is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (device is IDisposable disposable)
        {
            disposable.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
