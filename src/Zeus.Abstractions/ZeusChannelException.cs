namespace Zeus;

/// <summary>
/// 与某个命名通道相关的故障，例如端口被占用或写入时通道未打开。
/// </summary>
public sealed class ZeusChannelException : ZeusException
{
    /// <summary>
    /// 创建通道异常。
    /// </summary>
    /// <param name="channelName">出错的通道名。</param>
    /// <param name="message">面向开发者的说明。</param>
    public ZeusChannelException(string channelName, string message)
        : base(message)
    {
        ChannelName = channelName;
    }

    /// <summary>
    /// 创建带内部异常的通道异常。
    /// </summary>
    /// <param name="channelName">出错的通道名。</param>
    /// <param name="message">面向开发者的说明。</param>
    /// <param name="innerException">底层异常。</param>
    public ZeusChannelException(string channelName, string message, Exception innerException)
        : base(message, innerException)
    {
        ChannelName = channelName;
    }

    /// <summary>出错通道的注册名。</summary>
    public string ChannelName { get; }
}
