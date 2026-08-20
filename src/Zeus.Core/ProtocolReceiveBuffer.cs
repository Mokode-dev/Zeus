namespace Zeus;

/// <summary>
/// 协议客户端接收缓冲的公共上限与追加逻辑。
/// 对端若只推数据、永不形成合法帧，必须在此截断，避免进程被打到 OOM。
/// </summary>
/// <remarks>公开给各协议程序集复用；应用代码通常不必直接调用。</remarks>
public static class ProtocolReceiveBuffer
{
    /// <summary>单个协议客户端默认最大缓冲，1 MiB。</summary>
    public const int DefaultMaxBytes = 1024 * 1024;

    /// <summary>
    /// 把新到达的字节追加到缓冲。超过上限时清空并返回 <c>false</c>。
    /// </summary>
    /// <param name="buffer">累计接收缓冲。</param>
    /// <param name="data">本次到达的字节。</param>
    /// <param name="maxBytes">允许的最大字节数，必须大于 0。</param>
    /// <returns>追加成功为 <c>true</c>；超限已清空为 <c>false</c>。</returns>
    public static bool TryAppend(List<byte> buffer, ReadOnlySpan<byte> data, int maxBytes)
    {
        if (data.IsEmpty)
        {
            return true;
        }

        if (maxBytes <= 0 || buffer.Count > maxBytes - data.Length)
        {
            buffer.Clear();
            return false;
        }

        foreach (var value in data)
        {
            buffer.Add(value);
        }

        return true;
    }

    /// <summary>
    /// 超限时生成面向现场的协议异常，并完成正在等待的脉冲。
    /// </summary>
    public static ZeusProtocolException Overflow(string channelName, int maxBytes)
        => new($"通道 {channelName} 的接收缓冲超过 {maxBytes} 字节仍未形成完整帧，已丢弃以免内存耗尽。请检查对端协议、长度字段或是否把多客户端流量混进了同一通道。");
}
