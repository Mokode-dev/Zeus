using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 宿主级日志扩展。报文追踪默认关闭，避免未排障时把十六进制打进业务日志。
/// </summary>
public static class ZeusHostBuilderLoggingExtensions
{
    /// <summary>
    /// 把已登记和后续新增通道的 TX/RX 报文写入 <see cref="ILogger"/>。
    /// 热重载重建同名通道时会退订旧实例再挂到新实例，不会重复记录。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="level">报文日志级别，默认 Debug。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddCommunicationLogging(
        this ZeusHostBuilder builder,
        LogLevel level = LogLevel.Debug)
    {
        return builder.AddCommunicationLogging(options => options.Level = level);
    }

    /// <summary>
    /// 以选项回调启用通道报文结构化日志。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="configure">修改级别或分类名。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddCommunicationLogging(
        this ZeusHostBuilder builder,
        Action<CommunicationLoggingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new CommunicationLoggingOptions();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.Category))
        {
            throw new ZeusException("通信日志分类名不能为空。请使用例如 Zeus.Communication。");
        }

        builder.Services.Replace(ServiceDescriptor.Singleton(options));
        builder.Services.TryAddSingleton<ChannelCommunicationLogService>();
        return builder;
    }
}
