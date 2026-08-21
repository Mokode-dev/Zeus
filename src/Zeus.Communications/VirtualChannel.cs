using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 内存虚拟通道。默认把写入原样回显；传入 <see cref="IVirtualResponder"/> 时可模拟从站应答。
/// 回写推迟到写锁释放之后，避免协议客户端在 <see cref="ChannelBase.DataReceived"/> 里再写同一通道时自死锁。
/// </summary>
public sealed class VirtualChannel : ChannelBase
{
    private readonly IVirtualResponder? _responder;
    private readonly object _pendingLock = new();
    private readonly Queue<byte[]> _pendingReceives = [];

    /// <summary>
    /// 创建虚拟通道。
    /// </summary>
    /// <param name="name">宿主内唯一名称。</param>
    /// <param name="logger">诊断日志。</param>
    /// <param name="responder">可选对端。为 <c>null</c> 时回显写入内容。</param>
    public VirtualChannel(string name, ILogger<VirtualChannel>? logger = null, IVirtualResponder? responder = null)
        : base(name, logger)
    {
        _responder = responder;
    }

    /// <inheritdoc />
    protected override Task OpenCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override Task CloseCoreAsync(CancellationToken cancellationToken)
    {
        lock (_pendingLock)
        {
            _pendingReceives.Clear();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PublishPacketTrace(ChannelTraceDirection.Sent, buffer.Span);
        if (_responder is null)
        {
            EnqueueReceive(buffer.ToArray());
            return Task.CompletedTask;
        }

        return RespondAndEnqueueAsync(buffer, cancellationToken);
    }

    /// <summary>
    /// 询问虚拟从站并排队回写。异步应答可模拟延时；返回空表示丢包。
    /// </summary>
    private async Task RespondAndEnqueueAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var reply = await _responder!.RespondAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (reply is { } payload && !payload.IsEmpty)
        {
            EnqueueReceive(payload.ToArray());
        }
    }

    /// <inheritdoc />
    protected override void FlushDeferredReceive()
    {
        while (true)
        {
            byte[] payload;
            lock (_pendingLock)
            {
                if (_pendingReceives.Count == 0)
                {
                    return;
                }

                payload = _pendingReceives.Dequeue();
            }

            PublishData(payload);
        }
    }

    private void EnqueueReceive(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return;
        }

        lock (_pendingLock)
        {
            _pendingReceives.Enqueue(payload);
        }
    }
}
