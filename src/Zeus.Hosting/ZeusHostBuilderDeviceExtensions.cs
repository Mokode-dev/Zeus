namespace Zeus;

/// <summary>
/// 向宿主登记设备。通道必须先于设备注册，以便构建期就能解析到 <see cref="IChannel"/>。
/// </summary>
public static class ZeusHostBuilderDeviceExtensions
{
    /// <summary>
    /// 在指定通道上创建并登记设备。
    /// </summary>
    /// <typeparam name="TDevice">设备类型。</typeparam>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">设备名，后续 <c>Devices.Get</c> 使用。</param>
    /// <param name="channelName">已注册或即将在同一 <see cref="ZeusHost.Create"/> 中先注册的通道名。</param>
    /// <param name="factory">由名称与通道构造设备。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddDevice<TDevice>(
        this ZeusHostBuilder builder,
        string name,
        string channelName,
        Func<string, IChannel, TDevice> factory)
        where TDevice : class, IDevice
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Register((_, channels, devices) =>
        {
            IChannel channel;
            try
            {
                channel = channels.Get(channelName);
            }
            catch (ZeusException ex)
            {
                throw new ZeusException(
                    $"无法为设备 {name} 绑定通道 {channelName}：{ex.Message} 请先调用 AddSerialPort / AddTcpClient / AddVirtualChannel。",
                    ex);
            }

            devices.Add(factory(name, channel));
        });

        return builder;
    }
}
