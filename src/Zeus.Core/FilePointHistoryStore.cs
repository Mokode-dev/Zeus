using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Zeus;

/// <summary>
/// 把成功采样追加为 JSONL 文件。每行一条，采集线程只负责排队，实际写盘在后台串行完成。
/// </summary>
public sealed class FilePointHistoryStore : IPointHistoryStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    /// <summary>
    /// 创建文件历史存储。
    /// </summary>
    /// <param name="path">JSONL 文件路径。</param>
    public FilePointHistoryStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ZeusException("点历史文件路径不能为空。");
        }

        _path = System.IO.Path.GetFullPath(path);
    }

    /// <summary>
    /// 使用选项创建文件历史存储。
    /// </summary>
    /// <param name="options">路径选项。</param>
    public FilePointHistoryStore(FilePointHistoryStoreOptions options)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Path)
    {
    }

    /// <summary>实际写入的绝对路径。</summary>
    public string FilePath => _path;

    /// <inheritdoc />
    public async ValueTask AppendAsync(PointSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var line = JsonSerializer.Serialize(new FilePointHistoryRecord(
            snapshot.QualifiedName,
            snapshot.Definition.Name,
            snapshot.Definition.DeviceName,
            FormatValue(snapshot.Value),
            snapshot.UpdatedAt,
            snapshot.AlarmState.ToString()), JsonOptions);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.AppendAllTextAsync(_path, line + Environment.NewLine, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private static string? FormatValue(object? value)
        => value switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private sealed record FilePointHistoryRecord(
        string QualifiedName,
        string PointName,
        string DeviceName,
        string? Value,
        DateTimeOffset? UpdatedAt,
        string AlarmState);
}
