using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 把 JSON 工程配置装进宿主。通道与设备在构建期登记；采集间隔写入可热更新的选项单例。
/// </summary>
public static class ZeusHostBuilderConfigurationExtensions
{
    /// <summary>
    /// 从文件装载配置。默认监视该文件：保存后只热更新采集间隔，通道与设备需重启进程。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="path">JSON 路径。</param>
    /// <param name="watch">是否监视文件变化，默认 true。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddJsonFile(this ZeusHostBuilder builder, string path, bool watch = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var fullPath = Path.GetFullPath(path);
        Apply(builder, ZeusConfigurationLoader.LoadFile(fullPath));

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
    /// 监视器内部也走同一路径；测试或不想依赖 <c>FileSystemWatcher</c> 时可手动调用。
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
    /// 把已校验的配置应用到构建器。供装载与热更新共用采集写入逻辑。
    /// </summary>
    internal static void Apply(ZeusHostBuilder builder, ZeusAppConfiguration document)
    {
        ApplyAcquisition(builder.Acquisition, document.Acquisition);
        foreach (var channel in document.Channels)
        {
            ApplyChannel(builder, channel);
        }

        foreach (var device in document.Devices)
        {
            ApplyDevice(builder, device);
        }
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
        }
    }

    private static IVirtualResponder? CreateResponder(ChannelConfiguration channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Responder))
        {
            return null;
        }

        var transport = ZeusConfigurationLoader.Normalize(channel.Transport) == "tcp"
            ? ModbusTransport.Tcp
            : ModbusTransport.Rtu;
        return new ModbusSlaveResponder(channel.UnitId, transport);
    }

    private static void ApplyDevice(ZeusHostBuilder builder, DeviceConfiguration device)
    {
        var type = ZeusConfigurationLoader.Normalize(device.Type);
        var isTcp = type is "modbus-tcp" or "modbustcp" or "tcp";
        var timeout = device.TimeoutMilliseconds is { } ms
            ? TimeSpan.FromMilliseconds(ms)
            : (TimeSpan?)null;
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
            switch (table)
            {
                case "holding" or "holdingregister":
                    if (point.Scale is { } holdingScale)
                    {
                        map.HoldingRegister(point.Name, point.Address, raw => raw * holdingScale);
                    }
                    else
                    {
                        map.HoldingRegister(point.Name, point.Address);
                    }

                    break;
                case "input" or "inputregister":
                    if (point.Scale is { } inputScale)
                    {
                        map.InputRegister(point.Name, point.Address, raw => raw * inputScale);
                    }
                    else
                    {
                        map.InputRegister(point.Name, point.Address);
                    }

                    break;
                case "coil":
                    map.Coil(point.Name, point.Address);
                    break;
                case "discrete" or "discreteinput":
                    map.DiscreteInput(point.Name, point.Address);
                    break;
            }
        }
    }
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
/// 监视 JSON 文件。保存后重新读取，只把采集间隔写回运行中的 <see cref="AcquisitionOptions"/>。
/// 通道与设备拓扑变更会被拒绝并记日志，避免热插拔半开的串口。
/// </summary>
internal sealed class ZeusConfigurationWatchService : IDisposable, Microsoft.Extensions.Hosting.IHostedService
{
    private readonly ZeusConfigurationWatchOptions _watch;
    private readonly AcquisitionOptions _acquisition;
    private readonly ILogger<ZeusConfigurationWatchService> _logger;
    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new();
    private DateTime _lastWrite = DateTime.MinValue;

    /// <summary>
    /// 创建监视服务。
    /// </summary>
    public ZeusConfigurationWatchService(
        ZeusConfigurationWatchOptions watch,
        AcquisitionOptions acquisition,
        ILogger<ZeusConfigurationWatchService> logger)
    {
        _watch = watch;
        _acquisition = acquisition;
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
    public void Dispose() => _watcher.Dispose();

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
        try
        {
            await Task.Delay(80).ConfigureAwait(false);
            var document = ZeusConfigurationLoader.LoadFile(_watch.Path);
            ZeusHostBuilderConfigurationExtensions.ApplyAcquisition(_acquisition, document.Acquisition);
            _logger.LogInformation(
                "已热更新采集间隔为 {Interval} ms（来自 {Path}）。通道与设备拓扑变更需要重启进程。",
                document.Acquisition.IntervalMilliseconds,
                _watch.Path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "配置文件 {Path} 热更新失败，继续使用上一份有效采集间隔。", _watch.Path);
        }
    }
}
