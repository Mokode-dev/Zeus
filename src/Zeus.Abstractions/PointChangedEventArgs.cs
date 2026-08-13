namespace Zeus;

/// <summary>
/// 点表中某个点的值或错误状态发生变化。
/// </summary>
public sealed class PointChangedEventArgs : EventArgs
{
    /// <summary>
    /// 创建变化参数。
    /// </summary>
    /// <param name="previous">变化前的快照，首次采集时为 <c>null</c>。</param>
    /// <param name="current">变化后的快照。</param>
    public PointChangedEventArgs(PointSnapshot? previous, PointSnapshot current)
    {
        Previous = previous;
        Current = current ?? throw new ArgumentNullException(nameof(current));
    }

    /// <summary>变化前的快照。</summary>
    public PointSnapshot? Previous { get; }

    /// <summary>变化后的快照。</summary>
    public PointSnapshot Current { get; }
}
