using System.Globalization;

namespace Zeus;

/// <summary>
/// 面向业务的 Omron Host Link 设备：绑定通道与单元号，暴露常用字区读写。
/// 声明了点表后实现 <see cref="IAcquisitionSource"/>，由宿主采集循环自动轮询；
/// 标为可写的点实现 <see cref="IPointWriter"/>，可通过点表按名称下发。
/// </summary>
public sealed class HostLinkDevice : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly HostLinkClient _client;
    private readonly IReadOnlyList<HostLinkPointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>创建设备。通常由 <c>AddOmronHostLink</c> 构造。</summary>
    public HostLinkDevice(
        string name,
        IChannel channel,
        HostLinkOptions? options = null,
        TimeSpan? timeout = null,
        HostLinkPointMap? pointMap = null)
        : base(name, channel)
    {
        _client = new HostLinkClient(channel, options, timeout);
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs
            .Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable))
            .ToArray();
    }

    /// <summary>底层 Host Link 客户端。</summary>
    public HostLinkClient Client => _client;

    /// <inheritdoc />
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <summary>执行任意 Host Link 命令，返回不含结束码的数据区。</summary>
    public Task<string> ExecuteAsync(string command, string text, CancellationToken cancellationToken = default)
        => _client.ExecuteAsync(command, text, cancellationToken);

    /// <summary>读取字区。</summary>
    public Task<ushort[]> ReadWordsAsync(HostLinkArea area, ushort address, ushort count, CancellationToken cancellationToken = default)
        => _client.ReadWordsAsync(area, address, count, cancellationToken);

    /// <summary>写入字区。</summary>
    public Task WriteWordsAsync(HostLinkArea area, ushort address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteWordsAsync(area, address, values, cancellationToken);

    /// <summary>读取 DM 字。</summary>
    public Task<ushort[]> ReadDataMemoryWordsAsync(ushort address, ushort count, CancellationToken cancellationToken = default)
        => ReadWordsAsync(HostLinkArea.DataMemory, address, count, cancellationToken);

    /// <summary>写入 DM 字。</summary>
    public Task WriteDataMemoryWordsAsync(ushort address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => WriteWordsAsync(HostLinkArea.DataMemory, address, values, cancellationToken);

    /// <summary>读取 CIO 字。</summary>
    public Task<ushort[]> ReadCioWordsAsync(ushort address, ushort count, CancellationToken cancellationToken = default)
        => ReadWordsAsync(HostLinkArea.Cio, address, count, cancellationToken);

    /// <summary>写入 CIO 字。</summary>
    public Task WriteCioWordsAsync(ushort address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => WriteWordsAsync(HostLinkArea.Cio, address, values, cancellationToken);

    /// <inheritdoc />
    public async Task PollAsync(IPointTableWriter table, CancellationToken cancellationToken = default)
    {
        foreach (var group in GroupConsecutive(_specs))
        {
            try
            {
                await PublishGroupAsync(table, group, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                foreach (var spec in group)
                {
                    table.PublishError(Name + "." + spec.Name, ex.Message);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(string pointName, object value, IPointTableWriter table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(table);
        var spec = FindSpec(pointName);
        var qualified = Name + "." + spec.Name;
        try
        {
            if (!spec.Writable)
            {
                throw new ZeusException($"点 {qualified} 未标为可写。");
            }

            object published;
            if (spec.IsBit)
            {
                var bit = ConvertToBoolean(value, spec.Name);
                var words = await ReadWordsAsync(spec.Area, spec.Address, 1, cancellationToken).ConfigureAwait(false);
                if (bit)
                {
                    words[0] |= (ushort)(1 << spec.BitOffset);
                }
                else
                {
                    words[0] &= (ushort)~(1 << spec.BitOffset);
                }

                await WriteWordsAsync(spec.Area, spec.Address, words, cancellationToken).ConfigureAwait(false);
                published = bit;
            }
            else
            {
                var words = HostLinkCodec.EncodeValue(spec.DataType, value, spec.Scale, _client.Options.WordOrder);
                await WriteWordsAsync(spec.Area, spec.Address, words, cancellationToken).ConfigureAwait(false);
                published = HostLinkCodec.DecodeValue(spec.DataType, words, spec.BitOffset, spec.Scale, _client.Options.WordOrder);
            }

            table.Publish(qualified, published);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            table.PublishError(qualified, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private async Task PublishGroupAsync(IPointTableWriter table, IReadOnlyList<HostLinkPointSpec> group, CancellationToken cancellationToken)
    {
        var first = group[0];
        var lastAddress = group.Max(spec => spec.Address + spec.WordCount - 1);
        var count = checked((ushort)(lastAddress - first.Address + 1));
        var words = await ReadWordsAsync(first.Area, first.Address, count, cancellationToken).ConfigureAwait(false);
        foreach (var spec in group)
        {
            var offset = spec.Address - first.Address;
            table.Publish(
                Name + "." + spec.Name,
                HostLinkCodec.DecodeValue(
                    spec.DataType,
                    words.Skip(offset).Take(spec.WordCount).ToArray(),
                    spec.BitOffset,
                    spec.Scale,
                    _client.Options.WordOrder));
        }
    }

    private HostLinkPointSpec FindSpec(string pointName)
    {
        if (string.IsNullOrWhiteSpace(pointName))
        {
            throw new ZeusException("写回点名不能为空。");
        }

        var key = pointName.Trim();
        foreach (var spec in _specs)
        {
            if (string.Equals(spec.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                return spec;
            }
        }

        throw new ZeusException($"设备 {Name} 上找不到点 {key}。");
    }

    private static bool ConvertToBoolean(object value, string pointName)
    {
        if (value is bool bit)
        {
            return bit;
        }

        if (value is string text)
        {
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return number != 0;
            }
        }

        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new ZeusException($"点 {pointName} 需要布尔值，无法把 {value}（{value.GetType().Name}）写回。", ex);
        }
    }

    private static List<List<HostLinkPointSpec>> GroupConsecutive(IReadOnlyList<HostLinkPointSpec> specs)
    {
        var groups = new List<List<HostLinkPointSpec>>();
        foreach (var spec in specs.OrderBy(item => item.Area).ThenBy(item => item.Address).ThenBy(item => item.BitOffset))
        {
            if (groups.Count > 0)
            {
                var current = groups[^1];
                var first = current[0];
                var end = current.Max(item => item.Address + item.WordCount - 1);
                if (first.Area == spec.Area && spec.Address <= end + 1)
                {
                    current.Add(spec);
                    continue;
                }
            }

            groups.Add([spec]);
        }

        return groups;
    }
}
