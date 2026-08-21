using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 把 JSON 工程配置装进宿主。构建期登记通道与设备；监视开启后采集、重连与拓扑均可热更新。
/// 设备与虚拟从站由已登记的 <see cref="IZeusJsonBinder"/> 创建，配置包不再引用具体协议。
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
        var previous = state.Last ?? new ZeusAppConfiguration();
        try
        {
            await ApplyRuntimeAsync(host, previous, document, cancellationToken).ConfigureAwait(false);
            state.Last = document;
            state.Path = fullPath;
        }
        catch
        {
            // 中途失败时尽量把拓扑恢复到热更新前，避免「日志说沿用旧配置」但通道已被拆掉。
            try
            {
                await ApplyRuntimeAsync(host, document, previous, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            throw;
        }
    }

    /// <summary>
    /// 把已校验的配置应用到构建器。供装载与热更新共用采集写入逻辑。
    /// </summary>
    internal static void Apply(ZeusHostBuilder builder, ZeusAppConfiguration document)
    {
        ApplyAcquisition(builder.Acquisition, document.Acquisition);
        ApplyReconnect(builder.Reconnect, document.Reconnect);
        if (!string.IsNullOrWhiteSpace(document.PointHistoryFile))
        {
            builder.AddPointHistoryFile(document.PointHistoryFile);
        }

        foreach (var channel in document.Channels)
        {
            ApplyChannel(builder, channel);
        }

        foreach (var device in document.Devices)
        {
            RequireDeviceBinder(device).ApplyDevice(builder, device);
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
        options.SourceTimeout = TimeSpan.FromMilliseconds(configuration.SourceTimeoutMilliseconds);
        options.SerializePerChannel = configuration.SerializePerChannel;
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
                RequireDeviceBinder(pair.Value).ApplyDevice(host, pair.Value);
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
            "tcp-server" => Await(host.AddTcpServerAsync(name, options =>
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
            "udp-server" => Await(host.AddUdpServerAsync(name, options =>
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

    private static string ChannelFingerprint(ChannelConfiguration channel)
    {
        var type = ZeusConfigurationLoader.Normalize(channel.Type);
        return type switch
        {
            "virtual" => string.Join('|', type, ZeusConfigurationLoader.Normalize(channel.Responder), channel.UnitId, ZeusConfigurationLoader.Normalize(channel.Transport), channel.MeterAddress?.Trim(), channel.CommonAddress, channel.SnmpCommunity, channel.SnmpWriteCommunity),
            "serial" => string.Join('|', type, channel.PortName?.Trim(), channel.BaudRate),
            "tcp" => string.Join('|', type, channel.Host?.Trim(), channel.Port),
            "tcp-server" => string.Join('|', "tcp-server", channel.LocalAddress?.Trim(), EffectiveTcpServerPort(channel)),
            "udp" => string.Join('|', type, channel.Host?.Trim(), channel.Port, channel.LocalPort),
            "udp-server" => string.Join('|', "udp-server", channel.LocalAddress?.Trim(), EffectiveUdpServerPort(channel)),
            _ => type
        };
    }

    private static string DeviceFingerprint(DeviceConfiguration device)
    {
        var points = string.Join(';', device.Points.Select(ZeusConfigurationText.PointFingerprint));
        var binder = ZeusJsonBinders.FindDevice(ZeusConfigurationLoader.Normalize(device.Type));
        var protocol = binder?.DeviceFingerprint(device) ?? ZeusConfigurationLoader.Normalize(device.Type);
        return string.Join('|', device.Channel.Trim(), protocol, points);
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
            case "tcp-server":
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
            case "udp-server":
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

        var binder = ZeusJsonBinders.FindResponder(ZeusConfigurationLoader.Normalize(channel.Responder));
        return binder?.CreateResponder(channel);
    }

    private static IZeusJsonBinder RequireDeviceBinder(DeviceConfiguration device)
        => ZeusJsonBinders.FindDevice(ZeusConfigurationLoader.Normalize(device.Type))
            ?? throw new ZeusException($"设备类型 {device.Type} 没有对应的 JSON 绑定。请引用对应协议包。");
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
        if (!await _reload.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var host = _accessor.Host;
            if (host is null)
            {
                return;
            }

            await host.ReloadAsync(_watch.Path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ZeusLogEvents.ConfigurationReloadFailed, ex, "配置文件 {Path} 热更新失败，将沿用当前拓扑。", _watch.Path);
        }
        finally
        {
            _reload.Release();
        }
    }
}
