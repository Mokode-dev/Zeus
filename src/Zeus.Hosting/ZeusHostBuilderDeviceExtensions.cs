using Microsoft.Extensions.DependencyInjection;

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

        return builder.AddDevice(name, channelName, (_, deviceName, channel) => factory(deviceName, channel));
    }

    /// <summary>
    /// 在指定通道上创建并登记设备，工厂可从容器取日志或其它服务。
    /// </summary>
    /// <typeparam name="TDevice">设备类型。</typeparam>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">设备名，后续 <c>Devices.Get</c> 使用。</param>
    /// <param name="channelName">已注册或即将在同一 <see cref="ZeusHost.Create"/> 中先注册的通道名。</param>
    /// <param name="factory">由服务提供者、名称与通道构造设备。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddDevice<TDevice>(
        this ZeusHostBuilder builder,
        string name,
        string channelName,
        Func<IServiceProvider, string, IChannel, TDevice> factory)
        where TDevice : class, IDevice
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Register((services, channels, devices) =>
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

            devices.Add(factory(services, name, channel));
        });

        return builder;
    }

    /// <summary>
    /// 在已运行或已构建的宿主上登记设备。采集循环会在下一轮纳入其点表。
    /// </summary>
    /// <typeparam name="TDevice">设备类型。</typeparam>
    /// <param name="host">已构建的宿主。</param>
    /// <param name="name">设备名。</param>
    /// <param name="channelName">已存在的通道名。</param>
    /// <param name="factory">由名称与通道构造设备。</param>
    /// <returns>新登记的设备。</returns>
    public static TDevice AddDevice<TDevice>(
        this IZeusHost host,
        string name,
        string channelName,
        Func<string, IChannel, TDevice> factory)
        where TDevice : class, IDevice
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(factory);
        return host.AddDevice(name, channelName, (_, deviceName, bound) => factory(deviceName, bound));
    }

    /// <summary>
    /// 在已运行或已构建的宿主上登记设备，工厂可从容器取日志或其它服务。
    /// </summary>
    /// <typeparam name="TDevice">设备类型。</typeparam>
    /// <param name="host">已构建的宿主。</param>
    /// <param name="name">设备名。</param>
    /// <param name="channelName">已存在的通道名。</param>
    /// <param name="factory">由服务提供者、名称与通道构造设备。</param>
    /// <returns>新登记的设备。</returns>
    public static TDevice AddDevice<TDevice>(
        this IZeusHost host,
        string name,
        string channelName,
        Func<IServiceProvider, string, IChannel, TDevice> factory)
        where TDevice : class, IDevice
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(factory);

        IChannel channel;
        try
        {
            channel = host.Channels.Get(channelName);
        }
        catch (ZeusException ex)
        {
            throw new ZeusException(
                $"无法为设备 {name} 绑定通道 {channelName}：{ex.Message} 请先登记通道。",
                ex);
        }

        var device = factory(host.Services, name, channel);
        host.Devices.Add(device);
        return device;
    }

    /// <summary>
    /// 移除设备并从点表摘除其点。通道保持不动。
    /// </summary>
    /// <param name="host">已构建的宿主。</param>
    /// <param name="name">设备名。</param>
    /// <param name="cancellationToken">取消释放等待。</param>
    public static Task RemoveDeviceAsync(
        this IZeusHost host,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Devices.RemoveAsync(name, cancellationToken);
    }

    /// <summary>
    /// 移除通道。默认先卸载仍绑定该通道的设备，避免留下悬空引用。
    /// </summary>
    /// <param name="host">已构建的宿主。</param>
    /// <param name="name">通道名。</param>
    /// <param name="removeBoundDevices">为 <c>true</c> 时级联移除绑定设备；为 <c>false</c> 且仍有绑定时抛出。</param>
    /// <param name="cancellationToken">取消关闭等待。</param>
    public static async Task RemoveChannelAsync(
        this IZeusHost host,
        string name,
        bool removeBoundDevices = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var channel = host.Channels.Get(name);
        var bound = host.Devices.All
            .Where(device => ReferenceEquals(device.Channel, channel)
                || string.Equals(device.Channel.Name, channel.Name, StringComparison.OrdinalIgnoreCase))
            .Select(device => device.Name)
            .ToArray();

        if (bound.Length > 0 && !removeBoundDevices)
        {
            throw new ZeusException(
                $"通道 {name} 仍被设备 {string.Join("、", bound)} 使用。请先 RemoveDeviceAsync，或允许级联移除。");
        }

        foreach (var deviceName in bound)
        {
            await host.Devices.RemoveAsync(deviceName, cancellationToken).ConfigureAwait(false);
        }

        await host.Channels.RemoveAsync(name, cancellationToken).ConfigureAwait(false);
    }
}
