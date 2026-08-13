using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 将通道生命周期挂到 Generic Host：启动时按注册顺序打开，停止时按相反顺序关闭。
/// 单个通道失败不会阻断其余通道，但会记录错误，便于现场排错。
/// </summary>
internal sealed class ChannelLifecycleService : IHostedService
{
    private readonly ChannelRegistry _channels;
    private readonly ILogger<ChannelLifecycleService> _logger;

    /// <summary>
    /// 初始化生命周期服务。
    /// </summary>
    /// <param name="channels">已注册通道目录。</param>
    /// <param name="logger">诊断日志。</param>
    public ChannelLifecycleService(ChannelRegistry channels, ILogger<ChannelLifecycleService> logger)
    {
        _channels = channels;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var channel in _channels.All)
        {
            try
            {
                await channel.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动时打开通道 {Channel} 失败，其余通道将继续尝试。", channel.Name);
            }
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var channel in _channels.All.Reverse())
        {
            try
            {
                await channel.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "停止时关闭通道 {Channel} 失败。", channel.Name);
            }
        }
    }
}
