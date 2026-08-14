namespace Zeus;

/// <summary>
/// 设备目录增删事件参数。
/// </summary>
public sealed class DeviceRegistryChangedEventArgs : EventArgs
{
    /// <summary>
    /// 记录一次目录变更。
    /// </summary>
    /// <param name="change">增或删。</param>
    /// <param name="device">被操作的设备。</param>
    public DeviceRegistryChangedEventArgs(DeviceRegistryChange change, IDevice device)
    {
        Change = change;
        Device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <summary>变更种类。</summary>
    public DeviceRegistryChange Change { get; }

    /// <summary>被操作的设备。</summary>
    public IDevice Device { get; }
}
