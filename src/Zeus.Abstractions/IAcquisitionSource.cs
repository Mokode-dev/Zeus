namespace Zeus;

/// <summary>
/// 可被周期采集循环轮询的数据源。
/// Modbus 设备在声明了点表后实现本接口；自定义设备也可实现以接入同一循环。
/// </summary>
public interface IAcquisitionSource
{
    /// <summary>数据源名称，通常等于设备名。</summary>
    string Name { get; }

    /// <summary>本源贡献的点定义。循环启动时登记进点表。</summary>
    IReadOnlyList<PointDefinition> Points { get; }

    /// <summary>
    /// 执行一轮采集并把结果写入 <paramref name="table"/>。
    /// 实现应自行消化协议细节（例如把连续寄存器合并为一次读取）。
    /// </summary>
    /// <param name="table">点表写入器。</param>
    /// <param name="cancellationToken">取消本轮采集。</param>
    Task PollAsync(IPointTableWriter table, CancellationToken cancellationToken = default);
}
