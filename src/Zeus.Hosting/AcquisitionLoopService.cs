using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 宿主后台采集循环。按登记顺序轮询全部 <see cref="IAcquisitionSource"/>，
/// 单个源失败只记日志并写入点错误，不阻断其余源与下一轮。
/// </summary>
internal sealed class AcquisitionLoopService : BackgroundService
{
    private readonly DeviceRegistry _devices;
    private readonly PointTable _table;
    private readonly AcquisitionOptions _options;
    private readonly ILogger<AcquisitionLoopService> _logger;
    private IReadOnlyList<IAcquisitionSource> _sources = [];

    /// <summary>
    /// 初始化采集循环。真正的源在 <see cref="StartAsync"/> 时从设备目录收集，
    /// 因为设备是在 Generic Host 构建之后才登记的。
    /// </summary>
    /// <param name="devices">设备目录。</param>
    /// <param name="table">点表。</param>
    /// <param name="options">间隔与是否立即首轮。热更新会改同一实例，循环每轮读取最新值。</param>
    /// <param name="logger">诊断日志。</param>
    public AcquisitionLoopService(
        DeviceRegistry devices,
        PointTable table,
        AcquisitionOptions options,
        ILogger<AcquisitionLoopService> logger)
    {
        _devices = devices;
        _table = table;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _sources = _devices.All.OfType<IAcquisitionSource>().Where(source => source.Points.Count > 0).ToArray();
        foreach (var source in _sources)
        {
            foreach (var point in source.Points)
            {
                _table.Register(point);
            }
        }

        if (_sources.Count == 0)
        {
            return Task.CompletedTask;
        }

        return base.StartAsync(cancellationToken);
    }

    /// <summary>
    /// 读取当前间隔。非法热更新值回退到 500 毫秒，避免 Delay 抛出或空转打满 CPU。
    /// </summary>
    private TimeSpan CurrentInterval
        => _options.Interval > TimeSpan.Zero ? _options.Interval : TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_sources.Count == 0)
        {
            return;
        }

        if (!_options.PollImmediately)
        {
            try
            {
                await Task.Delay(CurrentInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var source in _sources)
            {
                try
                {
                    await source.PollAsync(_table, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
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

            try
            {
                await Task.Delay(CurrentInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
