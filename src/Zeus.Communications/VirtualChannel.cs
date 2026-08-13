using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 内存虚拟通道。默认把写入原样回显；传入 <see cref="IVirtualResponder"/> 时可模拟从站应答。
/// </summary>
public sealed class VirtualChannel : ChannelBase
{
    private readonly IVirtualResponder? _responder;

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
    protected override Task CloseCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_responder is null)
        {
            PublishData(buffer.Span);
            return Task.CompletedTask;
        }

        var reply = _responder.Respond(buffer);
        if (reply is { } payload && !payload.IsEmpty)
        {
            PublishData(payload.Span);
        }

        return Task.CompletedTask;
    }
}
