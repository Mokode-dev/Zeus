using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// Generic Host 释放时的通道兜底关闭。
/// 日常启停由 <see cref="ZeusHostRuntime"/> 负责，以便停止后仍可再次启动。
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
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
                _logger.LogWarning(ex, "释放宿主时关闭通道 {Channel} 失败。", channel.Name);
            }
        }
    }
}
