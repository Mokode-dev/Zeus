using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 基于 <see cref="UdpClient"/> 的 UDP 服务端通道。收到的每个数据报会作为一次通道接收事件发布。
/// <see cref="IChannel.WriteAsync"/> 仍回复最近一个发送方；需要指定对端时使用 <see cref="WriteAsync(EndPoint, ReadOnlyMemory{byte}, CancellationToken)"/>。
/// </summary>
public sealed class UdpServerChannel : ChannelBase, ISessionChannel
{
    private readonly UdpServerOptions _options;
    private readonly ConcurrentDictionary<string, IPEndPoint> _remotes = new(StringComparer.Ordinal);
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

    /// <summary>本通道见过的远端快照，按最近一次收到数据报登记。</summary>
    IReadOnlyList<EndPoint> ISessionChannel.RemoteEndPoints => RemoteEndPoints.Cast<EndPoint>().ToArray();

    /// <summary>本通道见过的远端快照，按最近一次收到数据报登记。</summary>
    public IReadOnlyList<IPEndPoint> RemoteEndPoints => _remotes.Values.ToArray();

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
        _remotes.Clear();
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
        _remotes.Clear();
    }

    /// <summary>
    /// 向指定远端写入数据报。对端不必先发过数据，但必须是可路由的 UDP 端点。
    /// </summary>
    /// <param name="remoteEndPoint">目标远端。</param>
    /// <param name="buffer">待发送数据。</param>
    /// <param name="cancellationToken">取消写入。</param>
    public async Task WriteAsync(EndPoint remoteEndPoint, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        if (State != ChannelState.Open)
        {
            throw new ZeusChannelException(
                Name,
                $"通道 {Name} 当前为 {State}，无法写入。请先调用宿主 StartAsync，或检查该通道是否已故障。");
        }

        var remote = ToIpEndPoint(remoteEndPoint);
        await WriteExclusiveAsync(
            async token =>
            {
                var client = _client ?? throw new ZeusChannelException(Name, $"通道 {Name} 的 UDP 服务端套接字已丢失，请重新启动宿主。");
                await client.SendAsync(buffer, remote, token).ConfigureAwait(false);
                RememberRemote(remote);
                PublishPacketTrace(ChannelTraceDirection.Sent, buffer.Span);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new ZeusChannelException(Name, $"通道 {Name} 的 UDP 服务端套接字已丢失，请重新启动宿主。");
        var remote = _lastRemoteEndPoint ?? throw new ZeusChannelException(
            Name,
            $"通道 {Name} 尚未收到任何 UDP 数据报，无法确定回复目标。请先等待客户端请求，使用带远端的 WriteAsync，或改用 UDP 客户端通道。");

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
                RememberRemote(result.RemoteEndPoint);
                PublishData(result.Buffer, result.RemoteEndPoint);
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

    private void RememberRemote(IPEndPoint remote)
    {
        _lastRemoteEndPoint = remote;
        _remotes[remote.ToString()] = remote;
    }

    private IPEndPoint ToIpEndPoint(EndPoint remoteEndPoint)
    {
        if (remoteEndPoint is IPEndPoint ip)
        {
            return ip;
        }

        throw new ZeusChannelException(
            Name,
            $"通道 {Name} 的 UDP 服务端只能向 IPEndPoint 写入，当前类型为 {remoteEndPoint.GetType().Name}。");
    }
}
