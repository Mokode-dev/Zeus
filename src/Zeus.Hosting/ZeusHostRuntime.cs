using Microsoft.Extensions.Hosting;

namespace Zeus;

/// <summary>
/// Generic Host 的薄封装。对外只暴露通道、设备与启停，隐藏 IHost 细节。
/// </summary>
internal sealed class ZeusHostRuntime : IZeusHost
{
    private readonly IHost _host;

    /// <summary>
    /// 使用已构建的 Generic Host 与目录创建运行时。
    /// </summary>
    /// <param name="host">底层宿主。</param>
    /// <param name="channels">通道目录。</param>
    /// <param name="devices">设备目录。</param>
    /// <param name="points">点表。</param>
    public ZeusHostRuntime(IHost host, IChannelRegistry channels, IDeviceRegistry devices, IPointTable points)
    {
        _host = host;
        Channels = channels;
        Devices = devices;
        Points = points;
    }

    /// <inheritdoc />
    public IChannelRegistry Channels { get; }

    /// <inheritdoc />
    public IDeviceRegistry Devices { get; }

    /// <inheritdoc />
    public IPointTable Points { get; }

    /// <inheritdoc />
    public IServiceProvider Services => _host.Services;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default) => _host.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default) => _host.StopAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (_host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _host.Dispose();
        }
    }
}
