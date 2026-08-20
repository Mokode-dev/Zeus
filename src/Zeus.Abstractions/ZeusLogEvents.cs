using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// Zeus 结构化日志的稳定事件编号。
/// 现场可按 Id 过滤通道生命周期、重连、采集、写回、配置热更新和报文追踪，而不依赖中文消息文本。
/// </summary>
public static class ZeusLogEvents
{
    /// <summary>通道已打开。</summary>
    public static readonly EventId ChannelOpened = new(1001, nameof(ChannelOpened));

    /// <summary>通道已关闭。</summary>
    public static readonly EventId ChannelClosed = new(1002, nameof(ChannelClosed));

    /// <summary>关闭通道时出现异常，状态仍会标为已关闭。</summary>
    public static readonly EventId ChannelCloseWarning = new(1003, nameof(ChannelCloseWarning));

    /// <summary>打开通道失败。宿主会继续启动，并由自动重连重试。</summary>
    public static readonly EventId ChannelOpenFailed = new(1004, nameof(ChannelOpenFailed));

    /// <summary>重开前清理底层传输资源时出现异常。</summary>
    public static readonly EventId ChannelCleanup = new(1005, nameof(ChannelCleanup));

    /// <summary>通道写入失败并进入故障态。</summary>
    public static readonly EventId ChannelWriteFailed = new(1006, nameof(ChannelWriteFailed));

    /// <summary>通道已故障，已排队一次自动重连。</summary>
    public static readonly EventId ReconnectScheduled = new(2001, nameof(ReconnectScheduled));

    /// <summary>自动重连成功，通道重新打开。</summary>
    public static readonly EventId ReconnectSucceeded = new(2002, nameof(ReconnectSucceeded));

    /// <summary>本轮自动重连失败，将继续退避。</summary>
    public static readonly EventId ReconnectFailed = new(2003, nameof(ReconnectFailed));

    /// <summary>采集源或单个点本轮读取失败。</summary>
    public static readonly EventId AcquisitionFailed = new(3001, nameof(AcquisitionFailed));

    /// <summary>按点名写回设备失败。</summary>
    public static readonly EventId PointWriteFailed = new(4001, nameof(PointWriteFailed));

    /// <summary>JSON 配置热更新已应用到运行中的拓扑。</summary>
    public static readonly EventId ConfigurationReloaded = new(5001, nameof(ConfigurationReloaded));

    /// <summary>JSON 配置热更新失败，继续使用上一份有效配置。</summary>
    public static readonly EventId ConfigurationReloadFailed = new(5002, nameof(ConfigurationReloadFailed));

    /// <summary>通道 TX/RX 原始报文。默认级别为 Debug，避免淹没业务日志。</summary>
    public static readonly EventId PacketTrace = new(6001, nameof(PacketTrace));
}
