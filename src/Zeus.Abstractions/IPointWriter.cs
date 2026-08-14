namespace Zeus;

/// <summary>
/// 能按点名把工程值写回设备的数据源。
/// 点表只负责解析点名并核对可写性；真正的协议下发由实现完成。
/// </summary>
public interface IPointWriter
{
    /// <summary>数据源名称，通常等于设备名。</summary>
    string Name { get; }

    /// <summary>
    /// 把一个点的工程值写到设备，成功后应更新 <paramref name="table"/>。
    /// </summary>
    /// <param name="pointName">设备内的短名，不含设备前缀。</param>
    /// <param name="value">工程值，例如 <c>80.0</c> 或 <c>true</c>。</param>
    /// <param name="table">点表写入器。成功请 <c>Publish</c>，失败请 <c>PublishError</c> 后再抛出。</param>
    /// <param name="cancellationToken">取消本次写入。</param>
    Task WriteAsync(
        string pointName,
        object value,
        IPointTableWriter table,
        CancellationToken cancellationToken = default);
}
