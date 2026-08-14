namespace Zeus;

/// <summary>
/// 注册 Modbus 设备与虚拟从站。通道必须先注册。
/// </summary>
public static class ZeusHostBuilderModbusExtensions
{
    /// <summary>
    /// 在已有通道上登记一台 Modbus RTU 设备。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="deviceName">设备名。</param>
    /// <param name="channelName">串口或虚拟通道名。</param>
    /// <param name="unitId">从站地址，默认 1。</param>
    /// <param name="timeout">应答超时。</param>
    /// <param name="points">可选点表。声明后由宿主采集循环自动轮询。</param>
    public static ZeusHostBuilder AddModbusRtu(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        byte unitId = 1,
        TimeSpan? timeout = null,
        Action<ModbusPointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (name, channel) =>
            new ModbusDevice(name, channel, unitId, ModbusTransport.Rtu, timeout, BuildMap(points)));
    }

    /// <summary>
    /// 在已有通道上登记一台 Modbus TCP 设备。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="deviceName">设备名。</param>
    /// <param name="channelName">TCP 或虚拟通道名。</param>
    /// <param name="unitId">单元标识，默认 1。</param>
    /// <param name="timeout">应答超时。</param>
    /// <param name="points">可选点表。声明后由宿主采集循环自动轮询。</param>
    public static ZeusHostBuilder AddModbusTcp(
        this ZeusHostBuilder builder,
        string deviceName,
        string channelName,
        byte unitId = 1,
        TimeSpan? timeout = null,
        Action<ModbusPointMap>? points = null)
    {
        return builder.AddDevice(deviceName, channelName, (name, channel) =>
            new ModbusDevice(name, channel, unitId, ModbusTransport.Tcp, timeout, BuildMap(points)));
    }

    /// <summary>
    /// 在已构建的宿主上登记一台 Modbus RTU 设备。采集循环会在下一轮纳入其点表。
    /// </summary>
    public static ModbusDevice AddModbusRtu(
        this IZeusHost host,
        string deviceName,
        string channelName,
        byte unitId = 1,
        TimeSpan? timeout = null,
        Action<ModbusPointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (name, channel) =>
            new ModbusDevice(name, channel, unitId, ModbusTransport.Rtu, timeout, BuildMap(points)));

    /// <summary>
    /// 在已构建的宿主上登记一台 Modbus TCP 设备。
    /// </summary>
    public static ModbusDevice AddModbusTcp(
        this IZeusHost host,
        string deviceName,
        string channelName,
        byte unitId = 1,
        TimeSpan? timeout = null,
        Action<ModbusPointMap>? points = null)
        => host.AddDevice(deviceName, channelName, (name, channel) =>
            new ModbusDevice(name, channel, unitId, ModbusTransport.Tcp, timeout, BuildMap(points)));

    private static ModbusPointMap? BuildMap(Action<ModbusPointMap>? configure)
    {
        if (configure is null)
        {
            return null;
        }

        var map = new ModbusPointMap();
        configure(map);
        return map;
    }
}
