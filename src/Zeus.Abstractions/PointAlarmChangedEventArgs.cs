namespace Zeus;

/// <summary>
/// 报警队列中某条记录发生变化。
/// </summary>
public sealed class PointAlarmChangedEventArgs : EventArgs
{
    /// <summary>
    /// 创建变化参数。
    /// </summary>
    /// <param name="previous">变化前的记录；首次产生时为 <c>null</c>。</param>
    /// <param name="current">变化后的记录。</param>
    public PointAlarmChangedEventArgs(PointAlarmRecord? previous, PointAlarmRecord current)
    {
        Previous = previous;
        Current = current ?? throw new ArgumentNullException(nameof(current));
    }

    /// <summary>变化前的记录。</summary>
    public PointAlarmRecord? Previous { get; }

    /// <summary>变化后的记录。</summary>
    public PointAlarmRecord Current { get; }
}
