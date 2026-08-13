namespace Zeus;

/// <summary>
/// 通道生命周期状态。
/// 状态迁移由框架内核推进，业务代码应订阅变化而不是自行猜测底层句柄是否有效。
/// </summary>
public enum ChannelState
{
    /// <summary>已创建但尚未尝试打开。</summary>
    Created = 0,

    /// <summary>正在打开底层传输（例如占用串口或建立套接字）。</summary>
    Opening = 1,

    /// <summary>传输已就绪，可以读写。</summary>
    Open = 2,

    /// <summary>发生不可恢复或尚未恢复的故障，通道不可用。</summary>
    Faulted = 3,

    /// <summary>已主动关闭，资源已释放。</summary>
    Closed = 4
}
