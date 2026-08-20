using System.Net;

namespace Zeus;

/// <summary>
/// 通道收到原始字节时的事件参数。
/// 载荷已从底层接收缓冲复制，订阅方可安全缓存或跨线程使用。
/// </summary>
public sealed class ChannelDataReceivedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化接收事件参数。远端未知时 <see cref="RemoteEndPoint"/> 为空。
    /// </summary>
    /// <param name="data">本次收到的完整拷贝。</param>
    public ChannelDataReceivedEventArgs(ReadOnlyMemory<byte> data)
        : this(data, null)
    {
    }

    /// <summary>
    /// 初始化带远端的接收事件参数。TCP/UDP 服务端应传入对端，便于按会话回写。
    /// </summary>
    /// <param name="data">本次收到的完整拷贝。</param>
    /// <param name="remoteEndPoint">发送本段数据的远端；客户端通道通常为 <c>null</c>。</param>
    public ChannelDataReceivedEventArgs(ReadOnlyMemory<byte> data, EndPoint? remoteEndPoint)
    {
        Data = data;
        RemoteEndPoint = remoteEndPoint;
    }

    /// <summary>本次收到的字节。框架保证其不与内部接收缓冲共享。</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// 发送本段数据的远端。TCP/UDP 服务端在收到数据时填充；串口、虚拟通道和客户端通道为 <c>null</c>。
    /// </summary>
    public EndPoint? RemoteEndPoint { get; }
}
