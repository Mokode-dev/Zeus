using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 面向业务的 Omron FINS 设备：绑定通道、FINS 路由字段与 UDP/TCP 封装，暴露内存区读写。
/// 声明了点表后实现 <see cref="IAcquisitionSource"/>，由宿主采集循环自动轮询；
/// 标为可写的点实现 <see cref="IPointWriter"/>，可通过点表按名称下发。
/// </summary>
public sealed class FinsDevice : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly FinsClient _client;
    private readonly IReadOnlyList<FinsPointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>创建设备。通常由 <c>AddOmronFins</c> / <c>AddOmronFinsUdp</c> / <c>AddOmronFinsTcp</c> 构造。</summary>
    public FinsDevice(
        string name,
        IChannel channel,
        FinsTransport transport,
        FinsOptions? options = null,
        TimeSpan? timeout = null,
        FinsPointMap? pointMap = null,
        ILogger<FinsDevice>? logger = null)
        : base(name, channel, logger)
    {
        _client = new FinsClient(channel, transport, options, timeout);
        Transport = transport;
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs
            .Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable))
            .ToArray();
    }

    /// <summary>底层 FINS 客户端。</summary>
    public FinsClient Client => _client;

    /// <summary>线上封装。</summary>
    public FinsTransport Transport { get; }

    /// <inheritdoc />
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <summary>执行任意 FINS 命令，返回不含命令码与结束码的数据区。</summary>
    public Task<byte[]> ExecuteAsync(ushort command, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => _client.ExecuteAsync(command, data, cancellationToken);

    /// <summary>读取字区。</summary>
    public Task<ushort[]> ReadWordsAsync(FinsMemoryAreaCode area, ushort address, ushort count, CancellationToken cancellationToken = default)
        => _client.ReadWordsAsync(area, address, count, cancellationToken);

    /// <summary>写入字区。</summary>
    public Task WriteWordsAsync(FinsMemoryAreaCode area, ushort address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteWordsAsync(area, address, values, cancellationToken);

    /// <summary>读取位区。</summary>
    public Task<bool[]> ReadBitsAsync(FinsMemoryAreaCode area, ushort address, byte bitOffset, ushort count, CancellationToken cancellationToken = default)
        => _client.ReadBitsAsync(area, address, bitOffset, count, cancellationToken);

    /// <summary>写入位区。</summary>
    public Task WriteBitsAsync(FinsMemoryAreaCode area, ushort address, byte bitOffset, IReadOnlyList<bool> values, CancellationToken cancellationToken = default)
        => _client.WriteBitsAsync(area, address, bitOffset, values, cancellationToken);

    /// <summary>写入单个位。</summary>
    public Task WriteBitAsync(FinsMemoryAreaCode area, ushort address, byte bitOffset, bool value, CancellationToken cancellationToken = default)
        => WriteBitsAsync(area, address, bitOffset, [value], cancellationToken);

    /// <summary>用同一个字填充一段字区。</summary>
    public Task FillWordsAsync(FinsMemoryAreaCode area, ushort address, ushort count, ushort value, CancellationToken cancellationToken = default)
        => _client.FillWordsAsync(area, address, count, value, cancellationToken);

    /// <summary>一次读取多个不连续地址。</summary>
    public Task<FinsMemoryValue[]> ReadMultipleAsync(IReadOnlyList<FinsMemoryAddress> addresses, CancellationToken cancellationToken = default)
        => _client.ReadMultipleAsync(addresses, cancellationToken);

    /// <summary>读取 DM 字。</summary>
    public Task<ushort[]> ReadDataMemoryWordsAsync(ushort address, ushort count, CancellationToken cancellationToken = default)
        => ReadWordsAsync(FinsMemoryAreaCode.DataMemoryWord, address, count, cancellationToken);

    /// <summary>写入 DM 字。</summary>
    public Task WriteDataMemoryWordsAsync(ushort address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => WriteWordsAsync(FinsMemoryAreaCode.DataMemoryWord, address, values, cancellationToken);

    /// <summary>读取 CIO 位。</summary>
    public Task<bool[]> ReadCioBitsAsync(ushort address, byte bitOffset, ushort count, CancellationToken cancellationToken = default)
        => ReadBitsAsync(FinsMemoryAreaCode.CioBit, address, bitOffset, count, cancellationToken);

    /// <summary>写入 CIO 位。</summary>
    public Task WriteCioBitsAsync(ushort address, byte bitOffset, IReadOnlyList<bool> values, CancellationToken cancellationToken = default)
        => WriteBitsAsync(FinsMemoryAreaCode.CioBit, address, bitOffset, values, cancellationToken);

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
                LogAcquisitionFailed(ex, group[0].Name);
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
                await WriteBitAsync(spec.Area, spec.Address, spec.BitOffset, bit, cancellationToken).ConfigureAwait(false);
                published = bit;
            }
            else
            {
                var words = FinsCodec.EncodeValue(spec.DataType, value, spec.Scale, _client.Options.WordOrder);
                await WriteWordsAsync(spec.Area, spec.Address, words, cancellationToken).ConfigureAwait(false);
                published = FinsCodec.DecodeValue(spec.DataType, words, spec.Scale, _client.Options.WordOrder);
            }

            table.Publish(qualified, published);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogWriteFailed(ex, spec.Name);
            table.PublishError(qualified, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private async Task PublishGroupAsync(IPointTableWriter table, IReadOnlyList<FinsPointSpec> group, CancellationToken cancellationToken)
    {
        var first = group[0];
        if (first.IsBit)
        {
            var count = (ushort)(group[^1].Address - first.Address + 1);
            var bits = await ReadBitsAsync(first.Area, first.Address, first.BitOffset, count, cancellationToken).ConfigureAwait(false);
            foreach (var spec in group)
            {
                table.Publish(Name + "." + spec.Name, bits[spec.Address - first.Address]);
            }

            return;
        }

        var wordCount = (ushort)(group[^1].Address + group[^1].WordCount - first.Address);
        var words = await ReadWordsAsync(first.Area, first.Address, wordCount, cancellationToken).ConfigureAwait(false);
        foreach (var spec in group)
        {
            var offset = spec.Address - first.Address;
            table.Publish(
                Name + "." + spec.Name,
                FinsCodec.DecodeValue(spec.DataType, words.Skip(offset).Take(spec.WordCount).ToArray(), spec.Scale, _client.Options.WordOrder));
        }
    }

    private FinsPointSpec FindSpec(string pointName)
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

    private static List<List<FinsPointSpec>> GroupConsecutive(IReadOnlyList<FinsPointSpec> specs)
    {
        var groups = new List<List<FinsPointSpec>>();
        foreach (var spec in specs.OrderBy(item => item.Area.Code).ThenBy(item => item.BitOffset).ThenBy(item => item.Address))
        {
            if (groups.Count > 0)
            {
                var current = groups[^1];
                var last = current[^1];
                if (CanAppend(current[0], last, spec))
                {
                    current.Add(spec);
                    continue;
                }
            }

            groups.Add([spec]);
        }

        return groups;
    }

    private static bool CanAppend(FinsPointSpec first, FinsPointSpec last, FinsPointSpec next)
    {
        if (first.Area.Code != next.Area.Code || first.IsBit != next.IsBit)
        {
            return false;
        }

        if (first.IsBit)
        {
            return first.BitOffset == next.BitOffset && next.Address == last.Address + 1;
        }

        return next.Address == last.Address + last.WordCount;
    }
}
