using System.Net;

namespace Zeus;

/// <summary>
/// 可按远端会话写入的通道。TCP/UDP 服务端实现本接口；
/// 未指定远端时 <see cref="IChannel.WriteAsync"/> 仍回复最近一次对端。
/// </summary>
public interface ISessionChannel : IChannel
{
    /// <summary>当前已知远端快照。UDP 为最近活跃对端，TCP 为已连接客户端。</summary>
    IReadOnlyList<EndPoint> RemoteEndPoints { get; }

    /// <summary>
    /// 向指定远端写入。对端不存在或已断开时抛出 <see cref="ZeusChannelException"/>。
    /// </summary>
    /// <param name="remoteEndPoint">目标远端。</param>
    /// <param name="buffer">待发送数据。</param>
    /// <param name="cancellationToken">取消写入。</param>
    Task WriteAsync(EndPoint remoteEndPoint, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
}
