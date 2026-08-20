using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 给目录中的每个通道挂上 <see cref="ChannelTraceLogger"/>。
/// 必须在 <see cref="ChannelSubscriptionMigrator"/> 之前订阅目录变更：
/// 热重载移除旧实例时先退订，避免报文处理器被迁到新实例后重复挂接。
/// </summary>
internal sealed class ChannelCommunicationLogService : IDisposable
{
    private readonly IChannelRegistry _channels;
    private readonly ILogger _logger;
    private readonly LogLevel _level;
    private readonly Dictionary<IChannel, ChannelTraceLogger> _attached = [];
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// 订阅通道目录并给当前已有通道挂上报文日志。
    /// </summary>
    /// <param name="channels">通道目录。</param>
    /// <param name="loggerFactory">用于创建分类日志器。</param>
    /// <param name="options">级别与分类名。</param>
    public ChannelCommunicationLogService(
        IChannelRegistry channels,
        ILoggerFactory loggerFactory,
        CommunicationLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(options);

        _channels = channels;
        _level = options.Level;
        var category = string.IsNullOrWhiteSpace(options.Category) ? "Zeus.Communication" : options.Category.Trim();
        _logger = loggerFactory.CreateLogger(category);
        _channels.Changed += OnChanged;
        foreach (var channel in _channels.All)
        {
            Attach(channel);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channels.Changed -= OnChanged;
        ChannelTraceLogger[] loggers;
        lock (_gate)
        {
            loggers = [.. _attached.Values];
            _attached.Clear();
        }

        foreach (var logger in loggers)
        {
            logger.Dispose();
        }
    }

    private void OnChanged(object? sender, ChannelRegistryChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.Change == ChannelRegistryChange.Removed)
        {
            Detach(e.Channel);
            return;
        }

        Attach(e.Channel);
    }

    private void Attach(IChannel channel)
    {
        lock (_gate)
        {
            if (_disposed || _attached.ContainsKey(channel))
            {
                return;
            }

            _attached[channel] = new ChannelTraceLogger(channel, _logger, _level);
        }
    }

    private void Detach(IChannel channel)
    {
        ChannelTraceLogger? logger;
        lock (_gate)
        {
            if (!_attached.Remove(channel, out logger))
            {
                return;
            }
        }

        logger.Dispose();
    }
}
