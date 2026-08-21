namespace Zeus;

/// <summary>
/// 一轮采集中发生的全部点变化。界面应按本事件刷新整表，而不是订阅每一个 <see cref="IPointTable.Changed"/>。
/// </summary>
public sealed class PointBatchChangedEventArgs : EventArgs
{
    /// <summary>
    /// 创建批次变化参数。
    /// </summary>
    /// <param name="changes">本轮按发生顺序排列的点变化；可能为空。</param>
    public PointBatchChangedEventArgs(IReadOnlyList<PointChangedEventArgs> changes)
    {
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    /// <summary>本轮变化，顺序与采集写入顺序一致。</summary>
    public IReadOnlyList<PointChangedEventArgs> Changes { get; }
}
