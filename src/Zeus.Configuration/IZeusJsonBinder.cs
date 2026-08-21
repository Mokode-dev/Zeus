namespace Zeus;

/// <summary>
/// JSON 工程配置中某一协议的绑定。由各协议程序集登记，配置包不再硬引用全部协议。
/// </summary>
public interface IZeusJsonBinder
{
    /// <summary>该绑定能处理的设备 <c>type</c> 规范值，例如 <c>modbus-rtu</c>。</summary>
    IReadOnlyList<string> DeviceTypes { get; }

    /// <summary>该绑定能处理的虚拟从站 <c>responder</c> 规范值；没有虚拟从站时为空。</summary>
    IReadOnlyList<string> ResponderTypes { get; }

    /// <summary>
    /// 校验一台设备及其点表。错误消息应带上 <paramref name="path"/>。
    /// </summary>
    /// <param name="device">设备配置。</param>
    /// <param name="path">例如 <c>zeus.json devices[0]</c>。</param>
    void ValidateDevice(DeviceConfiguration device, string path);

    /// <summary>
    /// 校验虚拟通道上的从站字段。
    /// </summary>
    /// <param name="channel">通道配置。</param>
    /// <param name="path">例如 <c>zeus.json channels[0]</c>。</param>
    void ValidateResponder(ChannelConfiguration channel, string path);

    /// <summary>
    /// 在构建器上登记设备。
    /// </summary>
    void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device);

    /// <summary>
    /// 在已构建的宿主上登记设备，供热更新使用。
    /// </summary>
    void ApplyDevice(IZeusHost host, DeviceConfiguration device);

    /// <summary>
    /// 按通道配置创建虚拟从站。无法处理时返回 <c>null</c>。
    /// </summary>
    IVirtualResponder? CreateResponder(ChannelConfiguration channel);

    /// <summary>
    /// 设备级指纹（不含点表）。点表由配置核心统一拼接。
    /// </summary>
    string DeviceFingerprint(DeviceConfiguration device);
}
