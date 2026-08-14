using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 基于 <see cref="UdpClient"/> 的 UDP 服务端通道。收到的每个数据报会作为一次 <see cref="ChannelBase.PublishData"/> 发布。
/// 写入时会回复最近一个发送方。
/// </summary>
public sealed class UdpServerChannel : ChannelBase
{
    private readonly UdpServerOptions _options;
    private UdpClient? _client;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;
    private IPEndPoint? _lastRemoteEndPoint;

    /// <summary>
    /// 创建 UDP 服务端通道。
    /// </summary>
    /// <param name="name">宿主内唯一名称。</param>
    /// <param name="options">监听参数。</param>
    /// <param name="logger">诊断日志。</param>
    public UdpServerChannel(string name, UdpServerOptions options, ILogger<UdpServerChannel>? logger = null)
        : base(name, logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var localAddress = CommunicationOptionGuard.LocalAddress(options.LocalAddress, Name, "UDP 服务端");
        _options = new UdpServerOptions
        {
            LocalAddress = localAddress.ToString(),
            LocalPort = CommunicationOptionGuard.LocalPort(options.LocalPort, Name, "UDP 服务端"),
            ReceiveBufferSize = CommunicationOptionGuard.NonNegativeBytes(
                options.ReceiveBufferSize,
                Name,
                nameof(UdpServerOptions.ReceiveBufferSize))
        };
    }

    /// <summary>当前绑定的本地端点。通道尚未打开时为 <c>null</c>。</summary>
    public IPEndPoint? LocalEndPoint => _client?.Client.LocalEndPoint as IPEndPoint;

    /// <summary>最近一次收到数据报的远端端点。尚未收到数据时为 <c>null</c>。</summary>
    public IPEndPoint? LastRemoteEndPoint => _lastRemoteEndPoint;

    /// <inheritdoc />
    protected override Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var localAddress = CommunicationOptionGuard.LocalAddress(_options.LocalAddress, Name, "UDP 服务端");
        var client = new UdpClient(new IPEndPoint(localAddress, _options.LocalPort));

        try
        {
            if (_options.ReceiveBufferSize > 0)
            {
                client.Client.ReceiveBufferSize = _options.ReceiveBufferSize;
            }
        }
        catch (Exception ex)
        {
            client.Dispose();
            throw new ZeusChannelException(
                Name,
                $"无法打开 UDP 服务端 {localAddress}:{_options.LocalPort}（通道 {Name}）：{ex.Message}。请确认端口未被占用。",
                ex);
        }

        _client = client;
        _lastRemoteEndPoint = null;
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
        _lastRemoteEndPoint = null;
    }

    /// <inheritdoc />
    protected override async Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new ZeusChannelException(Name, $"通道 {Name} 的 UDP 服务端套接字已丢失，请重新启动宿主。");
        var remote = _lastRemoteEndPoint ?? throw new ZeusChannelException(
            Name,
            $"通道 {Name} 尚未收到任何 UDP 数据报，无法确定回复目标。请先等待客户端请求，或改用 UDP 客户端通道。");

        await client.SendAsync(buffer, remote, cancellationToken).ConfigureAwait(false);
        PublishPacketTrace(ChannelTraceDirection.Sent, buffer.Span);
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                _lastRemoteEndPoint = result.RemoteEndPoint;
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
