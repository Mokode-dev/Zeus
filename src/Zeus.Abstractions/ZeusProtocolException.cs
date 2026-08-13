namespace Zeus;

/// <summary>
/// 协议层可预期故障，例如帧校验失败、从站异常码或请求超时。
/// </summary>
public class ZeusProtocolException : ZeusException
{
    /// <summary>
    /// 创建协议异常。
    /// </summary>
    /// <param name="message">面向开发者的说明，需包含原因与建议动作。</param>
    public ZeusProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 创建带内部异常的协议异常。
    /// </summary>
    /// <param name="message">面向开发者的说明。</param>
    /// <param name="innerException">底层异常。</param>
    public ZeusProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
