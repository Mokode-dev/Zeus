namespace Zeus;

/// <summary>
/// 点成功采样的可插拔落盘。默认不登记，点表只保留内存环形缓冲。
/// 实现应尽快返回，避免拖慢采集循环；失败应自行记录，不要抛回采集线程。
/// </summary>
public interface IPointHistoryStore
{
    /// <summary>
    /// 追加一次成功采样。采集循环在写入内存历史后调用。
    /// </summary>
    /// <param name="snapshot">成功采样快照。</param>
    /// <param name="cancellationToken">取消本次写入。</param>
    ValueTask AppendAsync(PointSnapshot snapshot, CancellationToken cancellationToken = default);
}
