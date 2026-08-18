using System.Globalization;

namespace Zeus;

/// <summary>
/// 面向业务的 Panasonic MEWTOCOL-COM 设备：绑定通道与站号，暴露常用数据寄存器和接点字读写。
/// 声明了点表后实现 <see cref="IAcquisitionSource"/>，由宿主采集循环自动轮询；
/// 标为可写的点实现 <see cref="IPointWriter"/>，可通过点表按名称下发。
/// </summary>
public sealed class MewtocolDevice : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly MewtocolClient _client;
    private readonly IReadOnlyList<MewtocolPointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>创建设备。通常由 <c>AddPanasonicMewtocol</c> 构造。</summary>
    public MewtocolDevice(
        string name,
        IChannel channel,
        MewtocolOptions? options = null,
        TimeSpan? timeout = null,
        MewtocolPointMap? pointMap = null)
        : base(name, channel)
    {
        _client = new MewtocolClient(channel, options, timeout);
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs
            .Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable))
            .ToArray();
    }

    /// <summary>底层 MEWTOCOL 客户端。</summary>
    public MewtocolClient Client => _client;

    /// <inheritdoc />
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <summary>执行任意 MEWTOCOL 命令，返回正常响应的数据区。</summary>
    public Task<string> ExecuteAsync(string command, string text, CancellationToken cancellationToken = default)
        => _client.ExecuteAsync(command, text, cancellationToken);

    /// <summary>读取 DT / LD / FL 数据寄存器字。</summary>
    public Task<ushort[]> ReadDataWordsAsync(MewtocolDataArea area, int address, int count, CancellationToken cancellationToken = default)
        => _client.ReadDataWordsAsync(area, address, count, cancellationToken);

    /// <summary>写入 DT / LD / FL 数据寄存器字。</summary>
    public Task WriteDataWordsAsync(MewtocolDataArea area, int address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteDataWordsAsync(area, address, values, cancellationToken);

    /// <summary>读取 X / Y / R / L 接点字块。</summary>
    public Task<ushort[]> ReadContactWordsAsync(MewtocolContactArea area, int wordAddress, int count, CancellationToken cancellationToken = default)
        => _client.ReadContactWordsAsync(area, wordAddress, count, cancellationToken);

    /// <summary>写入 Y / R / L 接点字块。</summary>
    public Task WriteContactWordsAsync(MewtocolContactArea area, int wordAddress, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteContactWordsAsync(area, wordAddress, values, cancellationToken);

    /// <summary>读取 DT 数据寄存器字。</summary>
    public Task<ushort[]> ReadDataRegistersAsync(int address, int count, CancellationToken cancellationToken = default)
        => ReadDataWordsAsync(MewtocolDataArea.DataRegister, address, count, cancellationToken);

    /// <summary>写入 DT 数据寄存器字。</summary>
    public Task WriteDataRegistersAsync(int address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => WriteDataWordsAsync(MewtocolDataArea.DataRegister, address, values, cancellationToken);

    /// <summary>读取 R 内部继电器接点字。</summary>
    public Task<ushort[]> ReadInternalRelayWordsAsync(int wordAddress, int count, CancellationToken cancellationToken = default)
        => ReadContactWordsAsync(MewtocolContactArea.InternalRelay, wordAddress, count, cancellationToken);

    /// <summary>写入 R 内部继电器接点字。</summary>
    public Task WriteInternalRelayWordsAsync(int wordAddress, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => WriteContactWordsAsync(MewtocolContactArea.InternalRelay, wordAddress, values, cancellationToken);

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
                var words = await ReadSpecWordsAsync(spec, 1, cancellationToken).ConfigureAwait(false);
                if (bit)
                {
                    words[0] |= (ushort)(1 << spec.BitOffset);
                }
                else
                {
                    words[0] &= (ushort)~(1 << spec.BitOffset);
                }

                await WriteSpecWordsAsync(spec, words, cancellationToken).ConfigureAwait(false);
                published = bit;
            }
            else
            {
                var words = MewtocolCodec.EncodeValue(spec.DataType, value, spec.Scale, _client.Options.WordOrder);
                await WriteSpecWordsAsync(spec, words, cancellationToken).ConfigureAwait(false);
                published = MewtocolCodec.DecodeValue(spec.DataType, words, spec.BitOffset, spec.Scale, _client.Options.WordOrder);
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

    private async Task PublishGroupAsync(IPointTableWriter table, IReadOnlyList<MewtocolPointSpec> group, CancellationToken cancellationToken)
    {
        var first = group[0];
        var lastAddress = group.Max(spec => spec.Address + spec.WordCount - 1);
        var count = checked(lastAddress - first.Address + 1);
        var words = await ReadSpecWordsAsync(first, count, cancellationToken).ConfigureAwait(false);
        foreach (var spec in group)
        {
            var offset = spec.Address - first.Address;
            table.Publish(
                Name + "." + spec.Name,
                MewtocolCodec.DecodeValue(
                    spec.DataType,
                    words.Skip(offset).Take(spec.WordCount).ToArray(),
                    spec.BitOffset,
                    spec.Scale,
                    _client.Options.WordOrder));
        }
    }

    private Task<ushort[]> ReadSpecWordsAsync(MewtocolPointSpec spec, int count, CancellationToken cancellationToken)
    {
        if (spec.DataArea is { } dataArea)
        {
            return ReadDataWordsAsync(dataArea, spec.Address, count, cancellationToken);
        }

        return ReadContactWordsAsync(spec.ContactArea!.Value, spec.Address, count, cancellationToken);
    }

    private Task WriteSpecWordsAsync(MewtocolPointSpec spec, IReadOnlyList<ushort> values, CancellationToken cancellationToken)
    {
        if (spec.DataArea is { } dataArea)
        {
            return WriteDataWordsAsync(dataArea, spec.Address, values, cancellationToken);
        }

        return WriteContactWordsAsync(spec.ContactArea!.Value, spec.Address, values, cancellationToken);
    }

    private MewtocolPointSpec FindSpec(string pointName)
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

    private static List<List<MewtocolPointSpec>> GroupConsecutive(IReadOnlyList<MewtocolPointSpec> specs)
    {
        var groups = new List<List<MewtocolPointSpec>>();
        foreach (var spec in specs.OrderBy(item => item.IsContact)
                     .ThenBy(item => item.DataArea)
                     .ThenBy(item => item.ContactArea)
                     .ThenBy(item => item.Address)
                     .ThenBy(item => item.BitOffset))
        {
            if (groups.Count > 0)
            {
                var current = groups[^1];
                var first = current[0];
                var end = current.Max(item => item.Address + item.WordCount - 1);
                if (first.DataArea == spec.DataArea
                    && first.ContactArea == spec.ContactArea
                    && spec.Address <= end + 1)
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
