using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 把全部通道的 TX/RX 报文写入 <see cref="ILogger"/> 的选项。
/// 由 <see cref="ZeusHostBuilderLoggingExtensions.AddCommunicationLogging(ZeusHostBuilder, Action{CommunicationLoggingOptions})"/> 登记。
/// </summary>
public sealed class CommunicationLoggingOptions
{
    /// <summary>
    /// 报文日志级别。默认 <see cref="LogLevel.Debug"/>，避免十六进制载荷冲掉 Information 业务日志。
    /// </summary>
    public LogLevel Level { get; set; } = LogLevel.Debug;

    /// <summary>
    /// 日志分类名。默认 <c>Zeus.Communication</c>，便于在配置里单独开关。
    /// </summary>
    public string Category { get; set; } = "Zeus.Communication";
}
