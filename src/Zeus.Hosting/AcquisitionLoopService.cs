using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 宿主后台采集循环。按登记顺序轮询全部 <see cref="IAcquisitionSource"/>，
/// 单个源失败只记日志并写入点错误，不阻断其余源与下一轮。
/// 设备目录变更会立即反映到下一轮；宿主停止时循环暂停，再次启动后恢复。
/// </summary>
internal sealed class AcquisitionLoopService : BackgroundService
{
    private readonly DeviceRegistry _devices;
    private readonly PointTable _table;
    private readonly AcquisitionOptions _options;
    private readonly HostRunState _runState;
    private readonly ILogger<AcquisitionLoopService> _logger;
    private readonly object _gate = new();
    private readonly HashSet<string> _registeredSources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化采集循环。点登记跟随设备目录变化，而不是只在首次启动时收集一次。
    /// </summary>
    /// <param name="devices">设备目录。</param>
    /// <param name="table">点表。</param>
    /// <param name="options">间隔与是否立即首轮。热更新会改同一实例，循环每轮读取最新值。</param>
    /// <param name="runState">宿主运行闸门。停止后循环等待，再次启动后继续。</param>
    /// <param name="logger">诊断日志。</param>
    public AcquisitionLoopService(
        DeviceRegistry devices,
        PointTable table,
        AcquisitionOptions options,
        HostRunState runState,
        ILogger<AcquisitionLoopService> logger)
    {
        _devices = devices;
        _table = table;
        _options = options;
        _runState = runState;
        _logger = logger;
        _devices.Changed += OnDevicesChanged;
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        SyncSources();
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _devices.Changed -= OnDevicesChanged;
        return base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// 读取当前间隔。非法热更新值回退到 500 毫秒，避免 Delay 抛出或空转打满 CPU。
    /// </summary>
    private TimeSpan CurrentInterval
        => _options.Interval > TimeSpan.Zero ? _options.Interval : TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var firstCycle = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            var wasPaused = !_runState.IsRunning;
            try
            {
                await _runState.WaitIfPausedAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            // 进程首次启动或停止后再启动，都按 PollImmediately 决定是否立刻采第一轮。
            if (wasPaused)
            {
                firstCycle = true;
            }

            if (firstCycle && !_options.PollImmediately)
            {
                firstCycle = false;
                if (!await DelayOrStopAsync(stoppingToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            firstCycle = false;
            var sources = SnapshotSources();
            if (sources.Count == 0)
            {
                if (!await DelayOrStopAsync(stoppingToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            var polls = sources.Select(source => PollOneAsync(source, stoppingToken)).ToArray();
            try
            {
                await Task.WhenAll(polls).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!await DelayOrStopAsync(stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <summary>
    /// 等待一个采集间隔；宿主暂停时提前结束本次等待，避免停机后仍空转一整轮。
    /// </summary>
    private async Task<bool> DelayOrStopAsync(CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _runState.PauseToken);
        try
        {
            await Task.Delay(CurrentInterval, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private void OnDevicesChanged(object? sender, DeviceRegistryChangedEventArgs e) => SyncSources();

    /// <summary>
    /// 按当前设备目录登记新点，并摘除已卸载设备的点。
    /// </summary>
    private void SyncSources()
    {
        lock (_gate)
        {
            var current = _devices.All.OfType<IAcquisitionSource>().Where(source => source.Points.Count > 0).ToArray();
            var currentNames = new HashSet<string>(current.Select(source => source.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var name in _registeredSources.ToArray())
            {
                if (currentNames.Contains(name))
                {
                    continue;
                }

                _table.UnregisterDevice(name);
                _registeredSources.Remove(name);
            }

            foreach (var source in current)
            {
                if (!_registeredSources.Add(source.Name))
                {
                    continue;
                }

                foreach (var point in source.Points)
                {
                    _table.Register(point);
                }
            }
        }
    }

    /// <summary>
    /// 轮询单个采集源。失败只记日志并写入点错误，不阻断其余源。
    /// </summary>
    private async Task PollOneAsync(IAcquisitionSource source, CancellationToken stoppingToken)
    {
        try
        {
            await source.PollAsync(_table, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "采集源 {Source} 本轮失败，将在下一轮重试。", source.Name);
            foreach (var point in source.Points)
            {
                _table.PublishError(point.QualifiedName, ex.Message);
            }
        }
    }

    private IReadOnlyList<IAcquisitionSource> SnapshotSources()
        => _devices.All.OfType<IAcquisitionSource>().Where(source => source.Points.Count > 0).ToArray();
}
