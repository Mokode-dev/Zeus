namespace Zeus;

/// <summary>
/// 通道状态迁移事件参数。
/// </summary>
public sealed class ChannelStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化状态变化参数。
    /// </summary>
    /// <param name="previous">迁移前的状态。</param>
    /// <param name="current">迁移后的状态。</param>
    /// <param name="error">导致进入故障态的异常；正常迁移时为 <c>null</c>。</param>
    public ChannelStateChangedEventArgs(ChannelState previous, ChannelState current, Exception? error = null)
    {
        Previous = previous;
        Current = current;
        Error = error;
    }

    /// <summary>迁移前的状态。</summary>
    public ChannelState Previous { get; }

    /// <summary>迁移后的状态。</summary>
    public ChannelState Current { get; }

    /// <summary>若本次迁移由故障触发，则为对应异常。</summary>
    public Exception? Error { get; }
}
