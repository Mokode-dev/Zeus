using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 基于 <see cref="TcpClient"/> 的客户端通道。接收循环在后台运行，字节通过 <see cref="ChannelBase.PublishData"/> 发布。
/// </summary>
public sealed class TcpClientChannel : ChannelBase
{
    private readonly TcpClientOptions _options;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;

    /// <summary>
    /// 创建 TCP 客户端通道。
    /// </summary>
    /// <param name="name">宿主内唯一名称。</param>
    /// <param name="options">连接参数。</param>
    /// <param name="logger">诊断日志。</param>
    public TcpClientChannel(string name, TcpClientOptions options, ILogger<TcpClientChannel>? logger = null)
        : base(name, logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = new TcpClientOptions
        {
            Host = options.Host,
            Port = options.Port,
            ConnectTimeoutMilliseconds = options.ConnectTimeoutMilliseconds
        };
    }

    /// <inheritdoc />
    protected override async Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        using var timeout = new CancellationTokenSource(_options.ConnectTimeoutMilliseconds);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            client.Dispose();
            throw new ZeusChannelException(
                Name,
                $"无法连接 {_options.Host}:{_options.Port}（通道 {Name}）：{ex.Message}。请确认对端已监听，或改用虚拟通道联调。",
                ex);
        }

        _client = client;
        _stream = client.GetStream();
        _receiveCts = new CancellationTokenSource();
        _receiveLoop = ReceiveLoopAsync(_receiveCts.Token);
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

        _stream?.Dispose();
        _client?.Dispose();
        _receiveCts?.Dispose();
        _stream = null;
        _client = null;
        _receiveLoop = null;
        _receiveCts = null;
    }

    /// <inheritdoc />
    protected override async Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new ZeusChannelException(Name, $"通道 {Name} 的套接字已丢失，请重新启动宿主。");
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var stream = _stream;
        if (stream is null)
        {
            return;
        }

        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    SetState(ChannelState.Faulted, new ZeusChannelException(Name, $"通道 {Name} 的对端已关闭连接。"));
                    return;
                }

                PublishData(buffer.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException)
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
