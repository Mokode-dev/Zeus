namespace Zeus;

/// <summary>
/// 配置周期采集。未调用时使用 500 毫秒间隔并立即执行第一轮。
/// </summary>
public static class ZeusHostBuilderAcquisitionExtensions
{
    /// <summary>
    /// 覆盖默认采集间隔。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="interval">两轮之间的等待时间，必须大于零。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddAcquisition(this ZeusHostBuilder builder, TimeSpan interval)
    {
        return builder.AddAcquisition(options => options.Interval = interval);
    }

    /// <summary>
    /// 以选项回调配置采集循环。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="configure">修改间隔或是否立即首轮。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddAcquisition(this ZeusHostBuilder builder, Action<AcquisitionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Acquisition);
        if (builder.Acquisition.Interval <= TimeSpan.Zero)
        {
            throw new ZeusException("采集间隔必须大于零。请使用例如 TimeSpan.FromMilliseconds(500)。");
        }

        return builder;
    }
}
