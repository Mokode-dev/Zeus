using System.Globalization;
using System.Text;

namespace Zeus;

/// <summary>
/// 通道报文文件日志器。适合现场故障复盘、长期通信抓取或把原始 TX/RX 记录交给外部工具分析。
/// </summary>
public sealed class ChannelTraceFileLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly IChannel _channel;
    private readonly StreamWriter _writer;
    private bool _disposed;

    /// <summary>
    /// 订阅一个通道，并把后续报文追踪追加写入文件。
    /// </summary>
    /// <param name="channel">要追踪的通道。</param>
    /// <param name="path">日志文件路径。父目录不存在时会自动创建。</param>
    /// <param name="append">为 <c>true</c> 时追加到现有文件；为 <c>false</c> 时覆盖重建。</param>
    public ChannelTraceFileLogger(IChannel channel, string path, bool append = true)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("通信日志文件路径不能为空。", nameof(path));
        }

        _channel = channel;
        FilePath = System.IO.Path.GetFullPath(path);

        var directory = System.IO.Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var mode = append ? FileMode.Append : FileMode.Create;
        var stream = new FileStream(FilePath, mode, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };

        _channel.PacketTraced += OnPacketTraced;
    }

    /// <summary>正在写入的日志文件绝对路径。</summary>
    public string FilePath { get; }

    /// <summary>被追踪的通道名。</summary>
    public string ChannelName => _channel.Name;

    /// <summary>
    /// 将报文记录格式化为一行制表符分隔文本：UTC 时间、通道名、方向、十六进制载荷。
    /// </summary>
    /// <param name="entry">报文记录。</param>
    public static string FormatLine(ChannelTraceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.Join(
            '\t',
            entry.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            entry.ChannelName,
            entry.Direction.ToString(),
            entry.Hex);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _channel.PacketTraced -= OnPacketTraced;
            _writer.Dispose();
        }
    }

    private void OnPacketTraced(object? sender, ChannelTraceEventArgs e)
    {
        var entry = new ChannelTraceEntry(_channel.Name, e.Direction, e.Data, e.Timestamp);
        var line = FormatLine(entry);

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine(line);
        }
    }
}
