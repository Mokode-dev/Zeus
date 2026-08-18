using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 把 JSON 工程配置装进宿主。构建期登记通道与设备；监视开启后采集、重连与拓扑均可热更新。
/// </summary>
public static class ZeusHostBuilderConfigurationExtensions
{
    /// <summary>
    /// 从文件装载配置。默认监视该文件：保存后热更新采集间隔、重连选项，以及通道/设备增删与参数变更。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="path">JSON 路径。</param>
    /// <param name="watch">是否监视文件变化，默认 true。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddJsonFile(this ZeusHostBuilder builder, string path, bool watch = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var fullPath = Path.GetFullPath(path);
        var document = ZeusConfigurationLoader.LoadFile(fullPath);
        Apply(builder, document);
        EnsureState(builder).Path = fullPath;

        if (watch)
        {
            builder.Services.AddSingleton(new ZeusConfigurationWatchOptions(fullPath));
            builder.Services.AddHostedService<ZeusConfigurationWatchService>();
        }

        return builder;
    }

    /// <summary>
    /// 从 JSON 文本装载配置，不监视文件。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="json">配置正文。</param>
    /// <param name="sourceName">错误消息中的来源名。</param>
    public static ZeusHostBuilder AddJson(this ZeusHostBuilder builder, string json, string sourceName = "配置")
    {
        ArgumentNullException.ThrowIfNull(builder);
        Apply(builder, ZeusConfigurationLoader.LoadJson(json, sourceName));
        return builder;
    }

    /// <summary>
    /// 从文件重新读取采集间隔并立即生效。通道与设备不会重建。
    /// 需要同步拓扑时请使用 <see cref="ReloadAsync"/>。
    /// </summary>
    /// <param name="host">已构建的宿主。</param>
    /// <param name="path">JSON 路径。</param>
    public static void ReloadAcquisition(this IZeusHost host, string path)
    {
        ArgumentNullException.ThrowIfNull(host);
        var options = host.Services.GetRequiredService<AcquisitionOptions>();
        var document = ZeusConfigurationLoader.LoadFile(path);
        ApplyAcquisition(options, document.Acquisition);
    }

    /// <summary>
    /// 从文件重新装载配置：更新采集与重连选项，并按差异增删通道、设备。
    /// 监视器内部也走同一路径；测试或不想依赖 <c>FileSystemWatcher</c> 时可手动调用。
    /// </summary>
    /// <param name="host">已构建的宿主。</param>
    /// <param name="path">JSON 路径。省略时使用 <see cref="AddJsonFile"/> 登记的路径。</param>
    /// <param name="cancellationToken">取消热更新。</param>
    public static async Task ReloadAsync(
        this IZeusHost host,
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var state = host.Services.GetRequiredService<ZeusConfigurationState>();
        var fullPath = Path.GetFullPath(path ?? state.Path ?? throw new ZeusException(
            "未指定配置文件路径。请传入 path，或先使用 AddJsonFile 装载。"));
        var document = ZeusConfigurationLoader.LoadFile(fullPath);
        await ApplyRuntimeAsync(host, state.Last, document, cancellationToken).ConfigureAwait(false);
        state.Last = document;
        state.Path = fullPath;
    }

    /// <summary>
    /// 把已校验的配置应用到构建器。供装载与热更新共用采集写入逻辑。
    /// </summary>
    internal static void Apply(ZeusHostBuilder builder, ZeusAppConfiguration document)
    {
        ApplyAcquisition(builder.Acquisition, document.Acquisition);
        ApplyReconnect(builder.Reconnect, document.Reconnect);
        foreach (var channel in document.Channels)
        {
            ApplyChannel(builder, channel);
        }

        foreach (var device in document.Devices)
        {
            ApplyDevice(builder, device);
        }

        EnsureState(builder).Last = document;
    }

    /// <summary>
    /// 仅更新采集选项。热更新走这条路径，避免重复登记通道。
    /// </summary>
    internal static void ApplyAcquisition(AcquisitionOptions options, AcquisitionConfiguration configuration)
    {
        if (configuration.IntervalMilliseconds <= 0)
        {
            throw new ZeusException("acquisition.intervalMilliseconds 必须大于 0。");
        }

        options.Interval = TimeSpan.FromMilliseconds(configuration.IntervalMilliseconds);
        options.PollImmediately = configuration.PollImmediately;
    }

    /// <summary>
    /// 把 JSON 中的重连选项写回运行中的单例。
    /// </summary>
    internal static void ApplyReconnect(ChannelReconnectOptions options, ReconnectConfiguration configuration)
    {
        options.Enabled = configuration.Enabled;
        options.InitialDelay = TimeSpan.FromMilliseconds(configuration.InitialDelayMilliseconds);
        options.MaxDelay = TimeSpan.FromMilliseconds(configuration.MaxDelayMilliseconds);
        options.BackoffMultiplier = configuration.BackoffMultiplier;
    }

    /// <summary>
    /// 按上一份与当前配置的差异，增删运行中的通道与设备。
    /// </summary>
    internal static async Task ApplyRuntimeAsync(
        IZeusHost host,
        ZeusAppConfiguration previous,
        ZeusAppConfiguration next,
        CancellationToken cancellationToken)
    {
        ApplyAcquisition(host.Services.GetRequiredService<AcquisitionOptions>(), next.Acquisition);
        ApplyReconnect(host.Services.GetRequiredService<ChannelReconnectOptions>(), next.Reconnect);

        var previousChannels = previous.Channels.ToDictionary(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        var nextChannels = next.Channels.ToDictionary(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        var previousDevices = previous.Devices.ToDictionary(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        var nextDevices = next.Devices.ToDictionary(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase);

        var replacedChannels = new HashSet<string>(
            previousChannels.Where(pair =>
                    nextChannels.TryGetValue(pair.Key, out var updated)
                    && ChannelFingerprint(pair.Value) != ChannelFingerprint(updated))
                .Select(pair => pair.Key),
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in previousDevices)
        {
            var shouldRemove = !nextDevices.TryGetValue(pair.Key, out var updated)
                || DeviceFingerprint(pair.Value) != DeviceFingerprint(updated)
                || replacedChannels.Contains(pair.Value.Channel.Trim())
                || !nextChannels.ContainsKey(pair.Value.Channel.Trim());
            if (shouldRemove && host.Devices.TryGet<IDevice>(pair.Key, out _))
            {
                await host.RemoveDeviceAsync(pair.Key, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var pair in previousChannels)
        {
            var shouldRemove = !nextChannels.ContainsKey(pair.Key) || replacedChannels.Contains(pair.Key);
            if (shouldRemove && host.Channels.TryGet(pair.Key, out _))
            {
                await host.RemoveChannelAsync(pair.Key, removeBoundDevices: true, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var pair in nextChannels)
        {
            if (!previousChannels.ContainsKey(pair.Key) || replacedChannels.Contains(pair.Key))
            {
                await AddRuntimeChannelAsync(host, pair.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var pair in nextDevices)
        {
            var shouldAdd = !previousDevices.TryGetValue(pair.Key, out var old)
                || DeviceFingerprint(old) != DeviceFingerprint(pair.Value)
                || replacedChannels.Contains(pair.Value.Channel.Trim());
            if (shouldAdd)
            {
                AddRuntimeDevice(host, pair.Value);
            }
        }
    }

    private static Task AddRuntimeChannelAsync(
        IZeusHost host,
        ChannelConfiguration channel,
        CancellationToken cancellationToken)
    {
        var name = channel.Name.Trim();
        return ZeusConfigurationLoader.Normalize(channel.Type) switch
        {
            "virtual" => Await(host.AddVirtualChannelAsync(name, CreateResponder(channel), cancellationToken)),
            "serial" => Await(host.AddSerialPortAsync(name, channel.PortName!, channel.BaudRate, cancellationToken)),
            "tcp" => Await(host.AddTcpClientAsync(name, channel.Host!, channel.Port, cancellationToken)),
            "tcp-server" or "tcpserver" => Await(host.AddTcpServerAsync(name, options =>
            {
                if (!string.IsNullOrWhiteSpace(channel.LocalAddress))
                {
                    options.LocalAddress = channel.LocalAddress;
                }

                options.LocalPort = EffectiveTcpServerPort(channel);
            }, cancellationToken)),
            "udp" => Await(host.AddUdpClientAsync(name, options =>
            {
                options.Host = channel.Host!;
                options.Port = channel.Port;
                options.LocalPort = channel.LocalPort;
            }, cancellationToken)),
            "udp-server" or "udpserver" => Await(host.AddUdpServerAsync(name, options =>
            {
                if (!string.IsNullOrWhiteSpace(channel.LocalAddress))
                {
                    options.LocalAddress = channel.LocalAddress;
                }

                options.LocalPort = EffectiveUdpServerPort(channel);
            }, cancellationToken)),
            _ => Task.CompletedTask
        };

        static async Task Await(Task task) => await task.ConfigureAwait(false);
    }

    private static void AddRuntimeDevice(IZeusHost host, DeviceConfiguration device)
    {
        var type = ZeusConfigurationLoader.Normalize(device.Type);
        var timeout = device.TimeoutMilliseconds is { } ms
            ? TimeSpan.FromMilliseconds(ms)
            : (TimeSpan?)null;

        if (ZeusConfigurationLoader.IsFinsDeviceType(type))
        {
            Action<FinsPointMap>? finsPoints = device.Points.Count == 0 ? null : map => ApplyFinsPoints(map, device.Points);
            host.AddOmronFins(device.Name.Trim(), device.Channel.Trim(), CreateFinsTransport(type), CreateFinsOptions(device), timeout, finsPoints);
            return;
        }

        if (ZeusConfigurationLoader.IsHostLinkDeviceType(type))
        {
            Action<HostLinkPointMap>? hostLinkPoints = device.Points.Count == 0 ? null : map => ApplyHostLinkPoints(map, device.Points);
            host.AddOmronHostLink(device.Name.Trim(), device.Channel.Trim(), CreateHostLinkOptions(device), timeout, hostLinkPoints);
            return;
        }

        if (ZeusConfigurationLoader.IsEtherNetIpDeviceType(type))
        {
            Action<EtherNetIpPointMap>? etherNetIpPoints = device.Points.Count == 0 ? null : map => ApplyEtherNetIpPoints(map, device.Points);
            host.AddEtherNetIp(device.Name.Trim(), device.Channel.Trim(), null, timeout, etherNetIpPoints);
            return;
        }

        if (ZeusConfigurationLoader.IsMcDeviceType(type))
        {
            Action<McPointMap>? mcPoints = device.Points.Count == 0 ? null : map => ApplyMcPoints(map, device.Points);
            host.AddMitsubishiMc(device.Name.Trim(), device.Channel.Trim(), CreateMcOptions(device), timeout, mcPoints);
            return;
        }

        if (ZeusConfigurationLoader.IsS7DeviceType(type))
        {
            Action<S7PointMap>? s7Points = device.Points.Count == 0 ? null : map => ApplyS7Points(map, device.Points);
            host.AddSiemensS7(device.Name.Trim(), device.Channel.Trim(), CreateS7Options(device), timeout, s7Points);
            return;
        }

        var isTcp = ZeusConfigurationLoader.IsModbusTcpDeviceType(type);
        Action<ModbusPointMap>? points = device.Points.Count == 0 ? null : map => ApplyPoints(map, device.Points);
        if (isTcp)
        {
            host.AddModbusTcp(device.Name.Trim(), device.Channel.Trim(), device.UnitId, timeout, points);
        }
        else
        {
            host.AddModbusRtu(device.Name.Trim(), device.Channel.Trim(), device.UnitId, timeout, points);
        }
    }

    private static string ChannelFingerprint(ChannelConfiguration channel)
    {
        var type = ZeusConfigurationLoader.Normalize(channel.Type);
        return type switch
        {
            "virtual" => string.Join('|', type, ZeusConfigurationLoader.Normalize(channel.Responder), channel.UnitId, ZeusConfigurationLoader.Normalize(channel.Transport)),
            "serial" => string.Join('|', type, channel.PortName?.Trim(), channel.BaudRate),
            "tcp" => string.Join('|', type, channel.Host?.Trim(), channel.Port),
            "tcp-server" or "tcpserver" => string.Join('|', "tcp-server", channel.LocalAddress?.Trim(), EffectiveTcpServerPort(channel)),
            "udp" => string.Join('|', type, channel.Host?.Trim(), channel.Port, channel.LocalPort),
            "udp-server" or "udpserver" => string.Join('|', "udp-server", channel.LocalAddress?.Trim(), EffectiveUdpServerPort(channel)),
            _ => type
        };
    }

    private static string DeviceFingerprint(DeviceConfiguration device)
    {
        var points = string.Join(';', device.Points.Select(point =>
            string.Join(':',
                point.Name,
                ZeusConfigurationLoader.Normalize(point.Table),
                ZeusConfigurationLoader.Normalize(point.DeviceCode),
                ZeusConfigurationLoader.Normalize(point.Area),
                ZeusConfigurationLoader.Normalize(point.TagName),
                ZeusConfigurationLoader.Normalize(point.Tag),
                ZeusConfigurationLoader.Normalize(point.DataType),
                point.DbNumber,
                point.Address,
                point.BitOffset,
                point.Scale,
                point.LowAlarmLimit,
                point.HighAlarmLimit,
                point.Writable)));
        var type = ZeusConfigurationLoader.Normalize(device.Type);
        if (ZeusConfigurationLoader.IsFinsDeviceType(type))
        {
            return string.Join('|',
                device.Channel.Trim(),
                type,
                device.TimeoutMilliseconds,
                device.DestinationNetwork,
                device.DestinationNode,
                device.DestinationUnit,
                device.SourceNetwork,
                device.SourceNode,
                device.SourceUnit,
                device.GatewayCount,
                device.InformationControlField,
                device.TcpRequestedClientNode,
                device.UseTcpNodeAddressHandshake,
                ZeusConfigurationLoader.Normalize(device.WordOrder),
                points);
        }

        if (ZeusConfigurationLoader.IsHostLinkDeviceType(type))
        {
            return string.Join('|',
                device.Channel.Trim(),
                type,
                device.UnitId,
                device.TimeoutMilliseconds,
                ZeusConfigurationLoader.Normalize(device.WordOrder),
                points);
        }

        if (ZeusConfigurationLoader.IsEtherNetIpDeviceType(type))
        {
            return string.Join('|',
                device.Channel.Trim(),
                type,
                device.TimeoutMilliseconds,
                points);
        }

        if (ZeusConfigurationLoader.IsMcDeviceType(type))
        {
            return string.Join('|',
                device.Channel.Trim(),
                type,
                device.TimeoutMilliseconds,
                ZeusConfigurationLoader.Normalize(device.FrameType),
                ZeusConfigurationLoader.Normalize(device.Encoding),
                device.NetworkNumber,
                device.PcNumber,
                device.IoNumber,
                device.StationNumber,
                device.MonitoringTimer,
                device.SerialNumber,
                points);
        }

        if (ZeusConfigurationLoader.IsS7DeviceType(type))
        {
            return string.Join('|',
                device.Channel.Trim(),
                type,
                device.TimeoutMilliseconds,
                device.Rack,
                device.Slot,
                device.LocalTsap,
                device.RemoteTsap,
                device.RequestedPduLength,
                points);
        }

        return string.Join('|', device.Channel.Trim(), type, device.UnitId, device.TimeoutMilliseconds, points);
    }

    private static ZeusConfigurationState EnsureState(ZeusHostBuilder builder)
    {
        var existing = builder.Services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(ZeusConfigurationState));
        if (existing?.ImplementationInstance is ZeusConfigurationState state)
        {
            return state;
        }

        state = new ZeusConfigurationState();
        builder.Services.AddSingleton(state);
        return state;
    }

    private static void ApplyChannel(ZeusHostBuilder builder, ChannelConfiguration channel)
    {
        var name = channel.Name.Trim();
        switch (ZeusConfigurationLoader.Normalize(channel.Type))
        {
            case "virtual":
                builder.AddVirtualChannel(name, CreateResponder(channel));
                break;
            case "serial":
                builder.AddSerialPort(name, channel.PortName!, channel.BaudRate);
                break;
            case "tcp":
                builder.AddTcpClient(name, channel.Host!, channel.Port);
                break;
            case "tcp-server" or "tcpserver":
                builder.AddTcpServer(name, options =>
                {
                    if (!string.IsNullOrWhiteSpace(channel.LocalAddress))
                    {
                        options.LocalAddress = channel.LocalAddress;
                    }

                    options.LocalPort = EffectiveTcpServerPort(channel);
                });
                break;
            case "udp":
                builder.AddUdpClient(name, options =>
                {
                    options.Host = channel.Host!;
                    options.Port = channel.Port;
                    options.LocalPort = channel.LocalPort;
                });
                break;
            case "udp-server" or "udpserver":
                builder.AddUdpServer(name, options =>
                {
                    if (!string.IsNullOrWhiteSpace(channel.LocalAddress))
                    {
                        options.LocalAddress = channel.LocalAddress;
                    }

                    options.LocalPort = EffectiveUdpServerPort(channel);
                });
                break;
        }
    }

    private static int EffectiveUdpServerPort(ChannelConfiguration channel)
        => channel.LocalPort != 0 ? channel.LocalPort : channel.Port;

    private static int EffectiveTcpServerPort(ChannelConfiguration channel)
        => channel.LocalPort != 0 ? channel.LocalPort : channel.Port;

    private static IVirtualResponder? CreateResponder(ChannelConfiguration channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Responder))
        {
            return null;
        }

        if (ZeusConfigurationLoader.Normalize(channel.Responder) is "mc" or "mitsubishi-mc" or "mitsubishimc")
        {
            return new McSlaveResponder();
        }

        if (ZeusConfigurationLoader.Normalize(channel.Responder) is "s7" or "siemens-s7" or "siemenss7")
        {
            return new S7SlaveResponder();
        }

        if (ZeusConfigurationLoader.Normalize(channel.Responder) is "fins" or "omron-fins" or "omronfins")
        {
            var finsTransport = ZeusConfigurationLoader.Normalize(channel.Transport) == "tcp"
                ? FinsTransport.Tcp
                : FinsTransport.Udp;
            return new FinsSlaveResponder(finsTransport);
        }

        if (ZeusConfigurationLoader.Normalize(channel.Responder) is "host-link" or "hostlink" or "omron-host-link" or "omronhostlink")
        {
            return new HostLinkSlaveResponder(channel.UnitId);
        }

        if (ZeusConfigurationLoader.Normalize(channel.Responder) is "ethernet-ip" or "ethernetip" or "cip" or "allen-bradley" or "allenbradley")
        {
            return new EtherNetIpSlaveResponder();
        }

        var transport = ZeusConfigurationLoader.Normalize(channel.Transport) == "tcp"
            ? ModbusTransport.Tcp
            : ModbusTransport.Rtu;
        return new ModbusSlaveResponder(channel.UnitId, transport);
    }

    private static void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
    {
        var type = ZeusConfigurationLoader.Normalize(device.Type);
        var timeout = device.TimeoutMilliseconds is { } ms
            ? TimeSpan.FromMilliseconds(ms)
            : (TimeSpan?)null;

        if (ZeusConfigurationLoader.IsFinsDeviceType(type))
        {
            Action<FinsPointMap>? finsPoints = device.Points.Count == 0 ? null : map => ApplyFinsPoints(map, device.Points);
            builder.AddOmronFins(device.Name.Trim(), device.Channel.Trim(), CreateFinsTransport(type), CreateFinsOptions(device), timeout, finsPoints);
            return;
        }

        if (ZeusConfigurationLoader.IsHostLinkDeviceType(type))
        {
            Action<HostLinkPointMap>? hostLinkPoints = device.Points.Count == 0 ? null : map => ApplyHostLinkPoints(map, device.Points);
            builder.AddOmronHostLink(device.Name.Trim(), device.Channel.Trim(), CreateHostLinkOptions(device), timeout, hostLinkPoints);
            return;
        }

        if (ZeusConfigurationLoader.IsEtherNetIpDeviceType(type))
        {
            Action<EtherNetIpPointMap>? etherNetIpPoints = device.Points.Count == 0 ? null : map => ApplyEtherNetIpPoints(map, device.Points);
            builder.AddEtherNetIp(device.Name.Trim(), device.Channel.Trim(), null, timeout, etherNetIpPoints);
            return;
        }

        if (ZeusConfigurationLoader.IsMcDeviceType(type))
        {
            Action<McPointMap>? mcPoints = device.Points.Count == 0 ? null : map => ApplyMcPoints(map, device.Points);
            builder.AddMitsubishiMc(device.Name.Trim(), device.Channel.Trim(), CreateMcOptions(device), timeout, mcPoints);
            return;
        }

        if (ZeusConfigurationLoader.IsS7DeviceType(type))
        {
            Action<S7PointMap>? s7Points = device.Points.Count == 0 ? null : map => ApplyS7Points(map, device.Points);
            builder.AddSiemensS7(device.Name.Trim(), device.Channel.Trim(), CreateS7Options(device), timeout, s7Points);
            return;
        }

        var isTcp = ZeusConfigurationLoader.IsModbusTcpDeviceType(type);
        Action<ModbusPointMap>? points = device.Points.Count == 0 ? null : map => ApplyPoints(map, device.Points);

        if (isTcp)
        {
            builder.AddModbusTcp(device.Name.Trim(), device.Channel.Trim(), device.UnitId, timeout, points);
        }
        else
        {
            builder.AddModbusRtu(device.Name.Trim(), device.Channel.Trim(), device.UnitId, timeout, points);
        }
    }

    private static void ApplyPoints(ModbusPointMap map, List<PointConfiguration> points)
    {
        foreach (var point in points)
        {
            var table = ZeusConfigurationLoader.Normalize(point.Table);
            var alarmLimits = CreateAlarmLimits(point);
            switch (table)
            {
                case "holding" or "holdingregister":
                    if (point.Scale is { } holdingScale)
                    {
                        map.HoldingRegister(point.Name, (ushort)point.Address, holdingScale);
                    }
                    else
                    {
                        map.HoldingRegister(point.Name, (ushort)point.Address);
                    }

                    ApplyAlarmLimits(map, point, alarmLimits);
                    ApplyWritable(map, point);
                    break;
                case "input" or "inputregister":
                    if (point.Scale is { } inputScale)
                    {
                        map.InputRegister(point.Name, (ushort)point.Address, inputScale);
                    }
                    else
                    {
                        map.InputRegister(point.Name, (ushort)point.Address);
                    }

                    ApplyAlarmLimits(map, point, alarmLimits);
                    break;
                case "coil":
                    map.Coil(point.Name, (ushort)point.Address);
                    ApplyWritable(map, point);
                    break;
                case "discrete" or "discreteinput":
                    map.DiscreteInput(point.Name, (ushort)point.Address);
                    break;
            }
        }
    }

    private static void ApplyMcPoints(McPointMap map, List<PointConfiguration> points)
    {
        foreach (var point in points)
        {
            var deviceCode = ZeusConfigurationLoader.ParseMcDeviceCode(point.DeviceCode ?? point.Table, $"point {point.Name}.deviceCode");
            var alarmLimits = CreateAlarmLimits(point);
            if (ZeusConfigurationLoader.IsMcWordDeviceCode(deviceCode))
            {
                if (point.Scale is { } scale)
                {
                    map.Word(point.Name, deviceCode, point.Address, scale);
                }
                else
                {
                    map.Word(point.Name, deviceCode, point.Address);
                }

                ApplyMcAlarmLimits(map, point, alarmLimits);
                ApplyMcWritable(map, point);
                continue;
            }

            map.Bit(point.Name, deviceCode, point.Address);
            ApplyMcWritable(map, point);
        }
    }

    private static void ApplyS7Points(S7PointMap map, List<PointConfiguration> points)
    {
        foreach (var point in points)
        {
            var area = ZeusConfigurationLoader.ParseS7Area(point.Area, $"point {point.Name}.area");
            var dataType = ZeusConfigurationLoader.ParseS7DataType(point.DataType, $"point {point.Name}.dataType");
            var alarmLimits = CreateAlarmLimits(point);
            if (point.Scale is { } scale)
            {
                map.ScaledPoint(point.Name, area, dataType, point.Address, scale, point.DbNumber, point.BitOffset, alarmLimits);
            }
            else
            {
                map.Point(point.Name, area, dataType, point.Address, point.DbNumber, point.BitOffset, alarmLimits);
            }

            ApplyS7Writable(map, point);
        }
    }

    private static void ApplyFinsPoints(FinsPointMap map, List<PointConfiguration> points)
    {
        foreach (var point in points)
        {
            var dataType = ZeusConfigurationLoader.ParseFinsDataType(point.DataType, $"point {point.Name}.dataType");
            var area = ZeusConfigurationLoader.ParseFinsMemoryAreaCode(point.Area ?? point.Table, dataType, $"point {point.Name}.area");
            var alarmLimits = CreateAlarmLimits(point);
            if (dataType == FinsDataType.Bit)
            {
                map.Bit(point.Name, area, (ushort)point.Address, (byte)point.BitOffset);
                ApplyFinsWritable(map, point);
                continue;
            }

            switch (dataType)
            {
                case FinsDataType.Word:
                    if (point.Scale is { } wordScale)
                    {
                        map.Word(point.Name, area, (ushort)point.Address, wordScale);
                    }
                    else
                    {
                        map.Word(point.Name, area, (ushort)point.Address);
                    }

                    break;
                case FinsDataType.Int16:
                    map.Int16(point.Name, area, (ushort)point.Address, point.Scale, alarmLimits);
                    break;
                case FinsDataType.UInt32:
                    map.UInt32(point.Name, area, (ushort)point.Address, point.Scale, alarmLimits);
                    break;
                case FinsDataType.Int32:
                    map.Int32(point.Name, area, (ushort)point.Address, point.Scale, alarmLimits);
                    break;
                case FinsDataType.Real:
                    map.Real(point.Name, area, (ushort)point.Address, point.Scale, alarmLimits);
                    break;
            }

            ApplyFinsAlarmLimits(map, point, alarmLimits);
            ApplyFinsWritable(map, point);
        }
    }

    private static void ApplyHostLinkPoints(HostLinkPointMap map, List<PointConfiguration> points)
    {
        foreach (var point in points)
        {
            var dataType = ZeusConfigurationLoader.ParseHostLinkDataType(point.DataType, $"point {point.Name}.dataType");
            var area = ZeusConfigurationLoader.ParseHostLinkArea(point.Area ?? point.Table, $"point {point.Name}.area");
            var alarmLimits = CreateAlarmLimits(point);
            if (dataType == HostLinkDataType.Bit)
            {
                map.Bit(point.Name, area, (ushort)point.Address, (byte)point.BitOffset);
                ApplyHostLinkWritable(map, point);
                continue;
            }

            switch (dataType)
            {
                case HostLinkDataType.Word:
                    if (point.Scale is { } wordScale)
                    {
                        map.Word(point.Name, area, (ushort)point.Address, wordScale);
                    }
                    else
                    {
                        map.Word(point.Name, area, (ushort)point.Address);
                    }

                    break;
                case HostLinkDataType.Int16:
                    map.Int16(point.Name, area, (ushort)point.Address, point.Scale, alarmLimits);
                    break;
                case HostLinkDataType.UInt32:
                    map.UInt32(point.Name, area, (ushort)point.Address, point.Scale, alarmLimits);
                    break;
                case HostLinkDataType.Int32:
                    map.Int32(point.Name, area, (ushort)point.Address, point.Scale, alarmLimits);
                    break;
                case HostLinkDataType.Real:
                    map.Real(point.Name, area, (ushort)point.Address, point.Scale, alarmLimits);
                    break;
            }

            ApplyHostLinkAlarmLimits(map, point, alarmLimits);
            ApplyHostLinkWritable(map, point);
        }
    }

    private static void ApplyEtherNetIpPoints(EtherNetIpPointMap map, List<PointConfiguration> points)
    {
        foreach (var point in points)
        {
            var dataType = ZeusConfigurationLoader.ParseEtherNetIpDataType(point.DataType, $"point {point.Name}.dataType");
            var tagName = string.IsNullOrWhiteSpace(point.TagName)
                ? string.IsNullOrWhiteSpace(point.Tag) ? point.Name : point.Tag!.Trim()
                : point.TagName!.Trim();
            var alarmLimits = CreateAlarmLimits(point);
            map.Tag(point.Name, tagName, dataType, point.Scale, alarmLimits);
            ApplyEtherNetIpAlarmLimits(map, point, alarmLimits);
            ApplyEtherNetIpWritable(map, point);
        }
    }

    private static PointAlarmLimits? CreateAlarmLimits(PointConfiguration point)
        => point.LowAlarmLimit is not null || point.HighAlarmLimit is not null
            ? new PointAlarmLimits(point.LowAlarmLimit, point.HighAlarmLimit)
            : null;

    private static void ApplyAlarmLimits(ModbusPointMap map, PointConfiguration point, PointAlarmLimits? alarmLimits)
    {
        if (alarmLimits is not null)
        {
            map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
        }
    }

    /// <summary>
    /// 把 JSON 中的 writable 落到点图。只读数据区已在装载时拒绝。
    /// </summary>
    private static void ApplyWritable(ModbusPointMap map, PointConfiguration point)
    {
        if (point.Writable)
        {
            map.Writable(point.Name);
        }
    }

    private static void ApplyMcAlarmLimits(McPointMap map, PointConfiguration point, PointAlarmLimits? alarmLimits)
    {
        if (alarmLimits is not null)
        {
            map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
        }
    }

    private static void ApplyMcWritable(McPointMap map, PointConfiguration point)
    {
        if (point.Writable)
        {
            map.Writable(point.Name);
        }
    }

    private static void ApplyS7Writable(S7PointMap map, PointConfiguration point)
    {
        if (point.Writable)
        {
            map.Writable(point.Name);
        }
    }

    private static void ApplyFinsAlarmLimits(FinsPointMap map, PointConfiguration point, PointAlarmLimits? alarmLimits)
    {
        if (alarmLimits is not null)
        {
            map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
        }
    }

    private static void ApplyFinsWritable(FinsPointMap map, PointConfiguration point)
    {
        if (point.Writable)
        {
            map.Writable(point.Name);
        }
    }

    private static void ApplyHostLinkAlarmLimits(HostLinkPointMap map, PointConfiguration point, PointAlarmLimits? alarmLimits)
    {
        if (alarmLimits is not null)
        {
            map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
        }
    }

    private static void ApplyHostLinkWritable(HostLinkPointMap map, PointConfiguration point)
    {
        if (point.Writable)
        {
            map.Writable(point.Name);
        }
    }

    private static void ApplyEtherNetIpAlarmLimits(EtherNetIpPointMap map, PointConfiguration point, PointAlarmLimits? alarmLimits)
    {
        if (alarmLimits is not null)
        {
            map.WithAlarmLimits(point.Name, alarmLimits.Low, alarmLimits.High);
        }
    }

    private static void ApplyEtherNetIpWritable(EtherNetIpPointMap map, PointConfiguration point)
    {
        if (point.Writable)
        {
            map.Writable(point.Name);
        }
    }

    private static Mc3EOptions CreateMcOptions(DeviceConfiguration device)
        => new()
        {
            FrameType = ZeusConfigurationLoader.ParseMcFrameType(device.FrameType, "device.frameType"),
            DataEncoding = ZeusConfigurationLoader.ParseMcDataEncoding(device.Encoding, "device.encoding"),
            NetworkNumber = (byte)device.NetworkNumber,
            PcNumber = (byte)device.PcNumber,
            IoNumber = (ushort)device.IoNumber,
            StationNumber = (byte)device.StationNumber,
            MonitoringTimer = (ushort)device.MonitoringTimer,
            SerialNumber = (ushort)device.SerialNumber
        };

    private static S7Options CreateS7Options(DeviceConfiguration device)
        => new()
        {
            Rack = (byte)device.Rack,
            Slot = (byte)device.Slot,
            LocalTsap = (ushort)device.LocalTsap,
            RemoteTsap = device.RemoteTsap is { } remoteTsap ? (ushort)remoteTsap : null,
            RequestedPduLength = (ushort)device.RequestedPduLength
        };

    private static FinsTransport CreateFinsTransport(string normalizedType)
        => ZeusConfigurationLoader.IsFinsTcpDeviceType(normalizedType) ? FinsTransport.Tcp : FinsTransport.Udp;

    private static FinsOptions CreateFinsOptions(DeviceConfiguration device)
        => new()
        {
            DestinationNetwork = (byte)device.DestinationNetwork,
            DestinationNode = (byte)device.DestinationNode,
            DestinationUnit = (byte)device.DestinationUnit,
            SourceNetwork = (byte)device.SourceNetwork,
            SourceNode = (byte)device.SourceNode,
            SourceUnit = (byte)device.SourceUnit,
            GatewayCount = (byte)device.GatewayCount,
            InformationControlField = (byte)device.InformationControlField,
            TcpRequestedClientNode = (byte)device.TcpRequestedClientNode,
            UseTcpNodeAddressHandshake = device.UseTcpNodeAddressHandshake,
            WordOrder = ZeusConfigurationLoader.ParseFinsWordOrder(device.WordOrder, "device.wordOrder")
        };

    private static HostLinkOptions CreateHostLinkOptions(DeviceConfiguration device)
        => new()
        {
            UnitNumber = device.UnitId,
            WordOrder = ZeusConfigurationLoader.ParseHostLinkWordOrder(device.WordOrder, "device.wordOrder")
        };
}

/// <summary>
/// 配置监视所需的文件路径。由 <see cref="ZeusHostBuilderConfigurationExtensions.AddJsonFile"/> 登记。
/// </summary>
internal sealed class ZeusConfigurationWatchOptions
{
    /// <summary>
    /// 记录要监视的绝对路径。
    /// </summary>
    /// <param name="path">JSON 绝对路径。</param>
    public ZeusConfigurationWatchOptions(string path) => Path = path;

    /// <summary>被监视的文件。</summary>
    public string Path { get; }
}

/// <summary>
/// 监视 JSON 文件。保存后走 <see cref="ZeusHostBuilderConfigurationExtensions.ReloadAsync"/>，
/// 同步采集间隔、重连选项以及通道/设备拓扑。
/// </summary>
internal sealed class ZeusConfigurationWatchService : IDisposable, Microsoft.Extensions.Hosting.IHostedService
{
    private readonly ZeusConfigurationWatchOptions _watch;
    private readonly ZeusHostAccessor _accessor;
    private readonly ILogger<ZeusConfigurationWatchService> _logger;
    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new();
    private DateTime _lastWrite = DateTime.MinValue;
    private readonly SemaphoreSlim _reload = new(1, 1);

    /// <summary>
    /// 创建监视服务。
    /// </summary>
    public ZeusConfigurationWatchService(
        ZeusConfigurationWatchOptions watch,
        ZeusHostAccessor accessor,
        ILogger<ZeusConfigurationWatchService> logger)
    {
        _watch = watch;
        _accessor = accessor;
        _logger = logger;
        var directory = Path.GetDirectoryName(watch.Path) ?? Directory.GetCurrentDirectory();
        var fileName = Path.GetFileName(watch.Path);
        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.Created += OnChanged;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _watcher.EnableRaisingEvents = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher.EnableRaisingEvents = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher.Dispose();
        _reload.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // 编辑器保存常连续触发两次；200ms 内的重复事件忽略。
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (now - _lastWrite < TimeSpan.FromMilliseconds(200))
            {
                return;
            }

            _lastWrite = now;
        }

        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        await _reload.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Delay(80).ConfigureAwait(false);
            var host = _accessor.Host;
            if (host is null)
            {
                return;
            }

            await host.ReloadAsync(_watch.Path).ConfigureAwait(false);
            _logger.LogInformation("已热更新配置 {Path}：采集间隔、重连选项与通道/设备拓扑已同步。", _watch.Path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "配置文件 {Path} 热更新失败，继续使用上一份有效配置。", _watch.Path);
        }
        finally
        {
            _reload.Release();
        }
    }
}
