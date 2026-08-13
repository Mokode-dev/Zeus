using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 基于 <see cref="UdpClient"/> 的 UDP 客户端通道。每个 UDP 数据报会作为一次 <see cref="ChannelBase.PublishData"/> 发布。
/// </summary>
public sealed class UdpClientChannel : ChannelBase
{
    private readonly UdpClientOptions _options;
    private UdpClient? _client;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;

    /// <summary>
    /// 创建 UDP 客户端通道。
    /// </summary>
    /// <param name="name">宿主内唯一名称。</param>
    /// <param name="options">连接参数。</param>
    /// <param name="logger">诊断日志。</param>
    public UdpClientChannel(string name, UdpClientOptions options, ILogger<UdpClientChannel>? logger = null)
        : base(name, logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = new UdpClientOptions
        {
            Host = options.Host,
            Port = options.Port,
            LocalPort = options.LocalPort,
            ReceiveBufferSize = options.ReceiveBufferSize
        };
    }

    /// <inheritdoc />
    protected override Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = _options.LocalPort == 0
            ? new UdpClient()
            : new UdpClient(_options.LocalPort);

        try
        {
            if (_options.ReceiveBufferSize > 0)
            {
                client.Client.ReceiveBufferSize = _options.ReceiveBufferSize;
            }

            client.Connect(_options.Host, _options.Port);
        }
        catch (Exception ex)
        {
            client.Dispose();
            throw new ZeusChannelException(
                Name,
                $"无法打开 UDP {_options.Host}:{_options.Port}（通道 {Name}）：{ex.Message}。请确认主机名、端口与本地端口占用。",
                ex);
        }

        _client = client;
        _receiveCts = new CancellationTokenSource();
        _receiveLoop = ReceiveLoopAsync(client, _receiveCts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task CloseCoreAsync(CancellationToken cancellationToken)
    {
        if (_receiveCts is not null)
        {
            await _receiveCts.CancelAsync().ConfigureAwait(false);
        }

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // 关闭阶段忽略接收循环异常，优先释放套接字。
            }
        }

        _client?.Dispose();
        _receiveCts?.Dispose();
        _client = null;
        _receiveLoop = null;
        _receiveCts = null;
    }

    /// <inheritdoc />
    protected override async Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new ZeusChannelException(Name, $"通道 {Name} 的 UDP 套接字已丢失，请重新启动宿主。");
        await client.SendAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                PublishData(result.Buffer);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetState(ChannelState.Faulted, ex);
            }
        }
    }
}
