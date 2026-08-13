namespace Zeus;

/// <summary>
/// 采集循环写入点表的窄接口。业务代码应使用 <see cref="IPointTable"/>，不要直接写点。
/// </summary>
public interface IPointTableWriter
{
    /// <summary>
    /// 登记一个尚未采集过的点，快照中值为空。
    /// </summary>
    /// <param name="definition">点定义。</param>
    void Register(PointDefinition definition);

    /// <summary>
    /// 写入一次成功的采样。值与上次相同且无错误时不会触发 <see cref="IPointTable.Changed"/>。
    /// </summary>
    /// <param name="qualifiedName">限定名。</param>
    /// <param name="value">新值。</param>
    void Publish(string qualifiedName, object? value);

    /// <summary>
    /// 记录一次采集失败。保留上一次成功值，便于界面显示「旧值 + 错误」。
    /// </summary>
    /// <param name="qualifiedName">限定名。</param>
    /// <param name="error">面向开发者的说明。</param>
    void PublishError(string qualifiedName, string error);
}
