namespace Zeus;

/// <summary>
/// 传输通道契约。
/// 通道只负责字节流的打开、关闭与读写，不理解协议帧；协议层应组合本接口而不是继承具体串口或套接字类型。
/// </summary>
public interface IChannel : IAsyncDisposable
{
    /// <summary>在宿主内唯一的通道名，查找与日志均以此为准。</summary>
    string Name { get; }

    /// <summary>当前生命周期状态。</summary>
    ChannelState State { get; }

    /// <summary>状态发生变化时触发，包括打开成功、主动关闭与故障。</summary>
    event EventHandler<ChannelStateChangedEventArgs>? StateChanged;

    /// <summary>底层收到数据时触发。事件在 IO 线程上发出，UI 适配器负责封送到界面线程。</summary>
    event EventHandler<ChannelDataReceivedEventArgs>? DataReceived;

    /// <summary>
    /// 通道收发原始报文时触发。适合接入滚动日志、通信诊断窗口或现场故障快照。
    /// </summary>
    event EventHandler<ChannelTraceEventArgs>? PacketTraced;

    /// <summary>
    /// 打开通道。对已打开的通道重复调用是幂等的。
    /// </summary>
    /// <param name="cancellationToken">取消打开过程。</param>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 关闭通道并释放底层资源。对已关闭通道重复调用是幂等的。
    /// </summary>
    /// <param name="cancellationToken">取消关闭等待；取消不会阻止尽力释放资源。</param>
    Task CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入原始字节。调用方必须保证通道处于 <see cref="ChannelState.Open"/>。
    /// </summary>
    /// <param name="buffer">待发送数据。</param>
    /// <param name="cancellationToken">取消写入。</param>
    Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
}
