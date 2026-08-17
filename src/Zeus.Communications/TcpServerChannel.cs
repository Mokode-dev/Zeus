using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 基于 <see cref="TcpListener"/> 的 TCP 服务端通道。所有客户端收到的数据都会作为 <see cref="ChannelBase.PublishData"/> 发布。
/// 写入时会回复最近一个发送数据的客户端。
/// </summary>
public sealed class TcpServerChannel : ChannelBase
{
    private readonly TcpServerOptions _options;
    private readonly ConcurrentDictionary<TcpClient, Task> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _acceptCts;
    private Task? _acceptLoop;
    private TcpClient? _lastClient;
    private IPEndPoint? _lastRemoteEndPoint;

    /// <summary>
    /// 创建 TCP 服务端通道。
    /// </summary>
    /// <param name="name">宿主内唯一名称。</param>
    /// <param name="options">监听参数。</param>
    /// <param name="logger">诊断日志。</param>
    public TcpServerChannel(string name, TcpServerOptions options, ILogger<TcpServerChannel>? logger = null)
        : base(name, logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var localAddress = CommunicationOptionGuard.LocalAddress(options.LocalAddress, Name, "TCP 服务端");
        if (options.Backlog <= 0)
        {
            throw new ZeusChannelException(
                Name,
                $"通道 {Name} 的 {nameof(TcpServerOptions.Backlog)} 必须大于 0。当前值：{options.Backlog}。");
        }

        _options = new TcpServerOptions
        {
            LocalAddress = localAddress.ToString(),
            LocalPort = CommunicationOptionGuard.LocalPort(options.LocalPort, Name, "TCP 服务端"),
            Backlog = options.Backlog,
            ReceiveBufferSize = CommunicationOptionGuard.NonNegativeBytes(
                options.ReceiveBufferSize,
                Name,
                nameof(TcpServerOptions.ReceiveBufferSize))
        };
    }

    /// <summary>当前绑定的本地端点。通道尚未打开时为 <c>null</c>。</summary>
    public IPEndPoint? LocalEndPoint => _listener?.LocalEndpoint as IPEndPoint;

    /// <summary>当前已连接客户端数量。</summary>
    public int ClientCount => _clients.Count;

    /// <summary>当前已连接客户端远端端点快照。</summary>
    public IReadOnlyList<IPEndPoint> RemoteEndPoints => _clients.Keys
        .Select(GetRemoteEndPoint)
        .Where(endpoint => endpoint is not null)
        .Select(endpoint => endpoint!)
        .ToArray();

    /// <summary>最近一次收到数据的远端端点。尚未收到数据时为 <c>null</c>。</summary>
    public IPEndPoint? LastRemoteEndPoint => _lastRemoteEndPoint;

    /// <summary>
    /// 向所有当前已连接客户端广播字节。
    /// </summary>
    /// <param name="buffer">待发送数据。</param>
    /// <param name="cancellationToken">取消写入。</param>
    public async Task BroadcastAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (State != ChannelState.Open)
        {
            throw new ZeusChannelException(
                Name,
                $"通道 {Name} 当前为 {State}，无法广播。请先调用宿主 StartAsync，或检查该通道是否已故障。");
        }

        var clients = _clients.Keys.ToArray();
        if (clients.Length == 0)
        {
            throw new ZeusChannelException(Name, $"通道 {Name} 当前没有已连接的 TCP 客户端，无法广播。");
        }

        var sent = 0;
        Exception? lastError = null;
        foreach (var client in clients)
        {
            if (!_clients.ContainsKey(client))
            {
                continue;
            }

            try
            {
                var stream = client.GetStream();
                await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                PublishPacketTrace(ChannelTraceDirection.Sent, buffer.Span);
                sent++;
            }
            catch (Exception ex) when (ex is not ZeusException)
            {
                lastError = ex;
                RemoveClient(client);
            }
        }

        if (sent == 0)
        {
            var message = $"通道 {Name} 没有可写入的 TCP 客户端，无法广播。请等待客户端重新连接。";
            if (lastError is null)
            {
                throw new ZeusChannelException(Name, message);
            }

            throw new ZeusChannelException(Name, message, lastError);
        }
    }

    /// <inheritdoc />
    protected override Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var localAddress = CommunicationOptionGuard.LocalAddress(_options.LocalAddress, Name, "TCP 服务端");
        var listener = new TcpListener(localAddress, _options.LocalPort);

        try
        {
            if (_options.ReceiveBufferSize > 0)
            {
                listener.Server.ReceiveBufferSize = _options.ReceiveBufferSize;
            }

            listener.Start(_options.Backlog);
        }
        catch (Exception ex)
        {
            listener.Stop();
            throw new ZeusChannelException(
                Name,
                $"无法打开 TCP 服务端 {localAddress}:{_options.LocalPort}（通道 {Name}）：{ex.Message}。请确认端口未被占用，防火墙已放行。",
                ex);
        }

        _listener = listener;
        _lastClient = null;
        _lastRemoteEndPoint = null;
        _acceptCts = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(listener, _acceptCts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task CloseCoreAsync(CancellationToken cancellationToken)
    {
        if (_acceptCts is not null)
        {
            await _acceptCts.CancelAsync().ConfigureAwait(false);
        }

        _listener?.Stop();

        foreach (var client in _clients.Keys)
        {
            client.Dispose();
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // 关闭阶段忽略接收循环异常，优先释放套接字。
            }
        }

        if (!_clients.IsEmpty)
        {
            try
            {
                await Task.WhenAll(_clients.Values).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // 客户端循环可能因套接字释放退出；关闭阶段不向外冒泡。
            }
        }

        _listener = null;
        _acceptLoop = null;
        _acceptCts?.Dispose();
        _acceptCts = null;
        _lastClient = null;
        _lastRemoteEndPoint = null;
        _clients.Clear();
    }

    /// <inheritdoc />
    protected override async Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var client = _lastClient ?? throw new ZeusChannelException(
            Name,
            $"通道 {Name} 尚未收到任何 TCP 客户端数据，无法确定回复目标。请先等待客户端请求，或改用 TCP 客户端通道。");

        if (!_clients.ContainsKey(client))
        {
            throw new ZeusChannelException(Name, $"通道 {Name} 最近的 TCP 客户端已断开，无法写入。请等待客户端重新发送请求。");
        }

        try
        {
            var stream = client.GetStream();
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            PublishPacketTrace(ChannelTraceDirection.Sent, buffer.Span);
        }
        catch (Exception ex) when (ex is not ZeusException)
        {
            RemoveClient(client);
            throw new ZeusChannelException(Name, $"通道 {Name} 写入 TCP 客户端失败：{ex.Message}。请等待客户端重新发送请求。", ex);
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                ConfigureClient(client);
                var placeholder = Task.CompletedTask;
                _clients[client] = placeholder;
                var loop = Task.Run(() => ReceiveLoopAsync(client, cancellationToken), CancellationToken.None);
                _clients.TryUpdate(client, loop, placeholder);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
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

    private async Task ReceiveLoopAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Max(_options.ReceiveBufferSize, 4096)];
        try
        {
            var stream = client.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    RemoveClient(client);
                    return;
                }

                _lastClient = client;
                _lastRemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                PublishData(buffer.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                RemoveClient(client);
            }
        }
        finally
        {
            RemoveClient(client);
        }
    }

    private void ConfigureClient(TcpClient client)
    {
        client.NoDelay = true;
        if (_options.ReceiveBufferSize > 0)
        {
            client.ReceiveBufferSize = _options.ReceiveBufferSize;
        }
    }

    private void RemoveClient(TcpClient client)
    {
        _clients.TryRemove(client, out _);
        if (ReferenceEquals(_lastClient, client))
        {
            _lastClient = null;
            _lastRemoteEndPoint = null;
        }

        client.Dispose();
    }

    private static IPEndPoint? GetRemoteEndPoint(TcpClient client)
    {
        try
        {
            return client.Client.RemoteEndPoint as IPEndPoint;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }
}
