namespace Zeus;

/// <summary>
/// Zeus 框架可预期故障的根异常。
/// 消息面向开发者，应直接给出可执行的下一步，而不是只复述内部类型名。
/// </summary>
public class ZeusException : Exception
{
    /// <summary>
    /// 使用说明性消息创建异常。
    /// </summary>
    /// <param name="message">面向开发者的说明，需包含原因与建议动作。</param>
    public ZeusException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 使用说明性消息与内部异常创建异常。
    /// </summary>
    /// <param name="message">面向开发者的说明。</param>
    /// <param name="innerException">底层异常。</param>
    public ZeusException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
