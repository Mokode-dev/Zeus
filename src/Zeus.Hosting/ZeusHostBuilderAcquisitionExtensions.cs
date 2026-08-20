using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// 配置通道故障后的自动重连。未调用时默认开启，首次等待 1 秒，上限 30 秒。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="configure">修改是否启用、初始等待与退避上限。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddReconnect(this ZeusHostBuilder builder, Action<ChannelReconnectOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Reconnect);
        if (builder.Reconnect.InitialDelay < TimeSpan.Zero)
        {
            throw new ZeusException("自动重连的首次等待不能为负数。");
        }

        if (builder.Reconnect.MaxDelay < TimeSpan.Zero)
        {
            throw new ZeusException("自动重连的等待上限不能为负数。");
        }

        if (builder.Reconnect.BackoffMultiplier < 1)
        {
            throw new ZeusException("自动重连的退避系数必须大于或等于 1。");
        }

        return builder;
    }

    /// <summary>
    /// 把成功采样追加到 JSONL 文件。未调用时点表只保留内存环形缓冲。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="path">文件路径，默认 <c>zeus-point-history.jsonl</c>。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddPointHistoryFile(this ZeusHostBuilder builder, string path = "zeus-point-history.jsonl")
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ZeusException("点历史文件路径不能为空。");
        }

        builder.Services.AddSingleton<IPointHistoryStore>(_ => new FilePointHistoryStore(path));
        return builder;
    }

    /// <summary>
    /// 登记自定义点历史存储。采集成功后由点表调用 <see cref="IPointHistoryStore.AppendAsync"/>。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="store">存储实现。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddPointHistoryStore(this ZeusHostBuilder builder, IPointHistoryStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);
        builder.Services.AddSingleton(store);
        return builder;
    }
}
