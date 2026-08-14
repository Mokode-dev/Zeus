using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 通道报文结构化日志器。订阅后把每条 TX/RX 原始报文写入 <see cref="ILogger"/>。
/// </summary>
public sealed class ChannelTraceLogger : IDisposable
{
    private readonly IChannel _channel;
    private readonly ILogger _logger;
    private readonly LogLevel _level;
    private bool _disposed;

    /// <summary>
    /// 订阅通道报文追踪，并以结构化字段写入日志。
    /// </summary>
    /// <param name="channel">要追踪的通道。</param>
    /// <param name="logger">目标日志器。</param>
    /// <param name="level">日志级别，默认 <see cref="LogLevel.Debug"/>。</param>
    public ChannelTraceLogger(IChannel channel, ILogger logger, LogLevel level = LogLevel.Debug)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _level = level;
        _channel.PacketTraced += OnPacketTraced;
    }

    /// <summary>被追踪的通道名。</summary>
    public string ChannelName => _channel.Name;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.PacketTraced -= OnPacketTraced;
    }

    private void OnPacketTraced(object? sender, ChannelTraceEventArgs e)
    {
        if (_disposed || !_logger.IsEnabled(_level))
        {
            return;
        }

        _logger.Log(
            _level,
            "通道 {Channel} {Direction} {ByteCount} 字节：{Hex}",
            _channel.Name,
            e.Direction,
            e.Data.Length,
            e.Hex);
    }
}
