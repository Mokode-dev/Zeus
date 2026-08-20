using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// Mitsubishi MC Protocol 设备封装。默认使用 3E Binary，可通过 <see cref="Mc3EOptions"/> 切换帧类型和编码。
/// 声明了点表后实现 <see cref="IAcquisitionSource"/>，由宿主采集循环自动轮询；
/// 标为可写的点实现 <see cref="IPointWriter"/>，可通过点表按名称下发。
/// </summary>
public sealed class McDevice : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly McClient _client;
    private readonly Mc3EOptions _options;
    private readonly IReadOnlyList<McPointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>
    /// 创建 MC 设备。通常由 <c>AddMitsubishiMc3E</c> 构造。
    /// </summary>
    /// <param name="name">设备名。</param>
    /// <param name="channel">传输通道。</param>
    /// <param name="options">MC 帧选项。</param>
    /// <param name="timeout">应答超时。</param>
    /// <param name="pointMap">可选点表。为 <c>null</c> 或不含点时不参与周期采集。</param>
    /// <param name="logger">诊断日志。宿主注册时自动注入。</param>
    public McDevice(
        string name,
        IChannel channel,
        Mc3EOptions? options = null,
        TimeSpan? timeout = null,
        McPointMap? pointMap = null,
        ILogger<McDevice>? logger = null)
        : base(name, channel, logger)
    {
        _client = new McClient(channel, options, timeout);
        _options = _client.Options;
        _specs = pointMap?.Points.ToArray() ?? [];
        EnsurePointMapSupported(_options, _specs);
        _points = _specs
            .Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable))
            .ToArray();
    }

    /// <summary>底层 MC 客户端。</summary>
    public McClient Client => _client;

    /// <inheritdoc />
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <summary>读取 D 数据寄存器。</summary>
    public Task<ushort[]> ReadDataRegistersAsync(int address, ushort points, CancellationToken cancellationToken = default)
        => _client.ReadWordsAsync(McDeviceCode.DataRegister, address, points, cancellationToken);

    /// <summary>写入 D 数据寄存器。</summary>
    public Task WriteDataRegistersAsync(int address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteWordsAsync(McDeviceCode.DataRegister, address, values, cancellationToken);

    /// <summary>读取 M 内部继电器。</summary>
    public Task<bool[]> ReadInternalRelaysAsync(int address, ushort points, CancellationToken cancellationToken = default)
        => _client.ReadBitsAsync(McDeviceCode.InternalRelay, address, points, cancellationToken);

    /// <summary>写入 M 内部继电器。</summary>
    public Task WriteInternalRelaysAsync(int address, IReadOnlyList<bool> values, CancellationToken cancellationToken = default)
        => _client.WriteBitsAsync(McDeviceCode.InternalRelay, address, values, cancellationToken);

    /// <summary>读取 X 输入继电器。</summary>
    public Task<bool[]> ReadInputRelaysAsync(int address, ushort points, CancellationToken cancellationToken = default)
        => _client.ReadBitsAsync(McDeviceCode.InputRelay, address, points, cancellationToken);

    /// <summary>读取 Y 输出继电器。</summary>
    public Task<bool[]> ReadOutputRelaysAsync(int address, ushort points, CancellationToken cancellationToken = default)
        => _client.ReadBitsAsync(McDeviceCode.OutputRelay, address, points, cancellationToken);

    /// <summary>写入 Y 输出继电器。</summary>
    public Task WriteOutputRelaysAsync(int address, IReadOnlyList<bool> values, CancellationToken cancellationToken = default)
        => _client.WriteBitsAsync(McDeviceCode.OutputRelay, address, values, cancellationToken);

    /// <summary>读取 W 链接寄存器。</summary>
    public Task<ushort[]> ReadLinkRegistersAsync(int address, ushort points, CancellationToken cancellationToken = default)
        => _client.ReadWordsAsync(McDeviceCode.LinkRegister, address, points, cancellationToken);

    /// <summary>写入 W 链接寄存器。</summary>
    public Task WriteLinkRegistersAsync(int address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteWordsAsync(McDeviceCode.LinkRegister, address, values, cancellationToken);

    /// <summary>读取 R 文件寄存器。</summary>
    public Task<ushort[]> ReadFileRegistersAsync(int address, ushort points, CancellationToken cancellationToken = default)
        => _client.ReadWordsAsync(McDeviceCode.FileRegister, address, points, cancellationToken);

    /// <summary>写入 R 文件寄存器。</summary>
    public Task WriteFileRegistersAsync(int address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteWordsAsync(McDeviceCode.FileRegister, address, values, cancellationToken);

    /// <summary>读取 ZR 扩展文件寄存器。</summary>
    public Task<ushort[]> ReadExtendedFileRegistersAsync(int address, ushort points, CancellationToken cancellationToken = default)
        => _client.ReadWordsAsync(McDeviceCode.ExtendedFileRegister, address, points, cancellationToken);

    /// <summary>写入 ZR 扩展文件寄存器。</summary>
    public Task WriteExtendedFileRegistersAsync(int address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteWordsAsync(McDeviceCode.ExtendedFileRegister, address, values, cancellationToken);

    /// <summary>读取字软元件。</summary>
    public Task<ushort[]> ReadWordsAsync(McDeviceCode deviceCode, int address, ushort points, CancellationToken cancellationToken = default)
        => _client.ReadWordsAsync(deviceCode, address, points, cancellationToken);

    /// <summary>写入字软元件。</summary>
    public Task WriteWordsAsync(McDeviceCode deviceCode, int address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        => _client.WriteWordsAsync(deviceCode, address, values, cancellationToken);

    /// <summary>读取位软元件。</summary>
    public Task<bool[]> ReadBitsAsync(McDeviceCode deviceCode, int address, ushort points, CancellationToken cancellationToken = default)
        => _client.ReadBitsAsync(deviceCode, address, points, cancellationToken);

    /// <summary>随机读取单字/双字软元件。仅 3E/4E 帧支持。</summary>
    public Task<McRandomReadResult> ReadRandomAsync(
        IReadOnlyList<McDeviceAddress> wordDevices,
        IReadOnlyList<McDeviceAddress>? doubleWordDevices = null,
        CancellationToken cancellationToken = default)
        => _client.ReadRandomAsync(wordDevices, doubleWordDevices, cancellationToken);

    /// <summary>写入位软元件。</summary>
    public Task WriteBitsAsync(McDeviceCode deviceCode, int address, IReadOnlyList<bool> values, CancellationToken cancellationToken = default)
        => _client.WriteBitsAsync(deviceCode, address, values, cancellationToken);

    /// <summary>随机写入单字/双字软元件。仅 3E/4E 帧支持。</summary>
    public Task WriteRandomWordsAsync(
        IReadOnlyList<McWordWrite> wordValues,
        IReadOnlyList<McDoubleWordWrite>? doubleWordValues = null,
        CancellationToken cancellationToken = default)
        => _client.WriteRandomWordsAsync(wordValues, doubleWordValues, cancellationToken);

    /// <summary>随机写入位软元件。仅 3E/4E 帧支持。</summary>
    public Task WriteRandomBitsAsync(IReadOnlyList<McBitWrite> values, CancellationToken cancellationToken = default)
        => _client.WriteRandomBitsAsync(values, cancellationToken);

    /// <summary>多块批量读取。仅 3E/4E 帧支持。</summary>
    public Task<McMultipleBlockReadResult> ReadMultipleBlocksAsync(
        IReadOnlyList<McDeviceRange> wordBlocks,
        IReadOnlyList<McDeviceRange>? bitBlocks = null,
        CancellationToken cancellationToken = default)
        => _client.ReadMultipleBlocksAsync(wordBlocks, bitBlocks, cancellationToken);

    /// <summary>远程 RUN。仅 3E/4E 帧支持。</summary>
    public Task RemoteRunAsync(CancellationToken cancellationToken = default)
        => _client.RemoteRunAsync(cancellationToken);

    /// <summary>远程 STOP。仅 3E/4E 帧支持。</summary>
    public Task RemoteStopAsync(CancellationToken cancellationToken = default)
        => _client.RemoteStopAsync(cancellationToken);

    /// <summary>远程 PAUSE。仅 3E/4E 帧支持。</summary>
    public Task RemotePauseAsync(CancellationToken cancellationToken = default)
        => _client.RemotePauseAsync(cancellationToken);

    /// <summary>远程锁存清除。仅 3E/4E 帧支持。</summary>
    public Task RemoteLatchClearAsync(CancellationToken cancellationToken = default)
        => _client.RemoteLatchClearAsync(cancellationToken);

    /// <summary>远程复位。仅 3E/4E 帧支持。</summary>
    public Task RemoteResetAsync(CancellationToken cancellationToken = default)
        => _client.RemoteResetAsync(cancellationToken);

    /// <inheritdoc />
    public async Task WriteAsync(
        string pointName,
        object value,
        IPointTableWriter table,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(table);
        var spec = FindSpec(pointName);
        var qualified = Name + "." + spec.Name;
        try
        {
            var published = await WriteSpecAsync(spec, value, cancellationToken).ConfigureAwait(false);
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
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private async Task PublishGroupAsync(
        IPointTableWriter table,
        IReadOnlyList<McPointSpec> group,
        CancellationToken cancellationToken)
    {
        var first = group[0];
        var quantity = (ushort)(group[^1].Address - first.Address + 1);
        if (first.IsWord)
        {
            var values = await ReadWordsAsync(first.DeviceCode, first.Address, quantity, cancellationToken)
                .ConfigureAwait(false);
            foreach (var spec in group)
            {
                var raw = values[spec.Address - first.Address];
                table.Publish(Name + "." + spec.Name, spec.Convert is null ? raw : spec.Convert(raw));
            }

            return;
        }

        var bits = await ReadBitsAsync(first.DeviceCode, first.Address, quantity, cancellationToken)
            .ConfigureAwait(false);
        foreach (var spec in group)
        {
            table.Publish(Name + "." + spec.Name, bits[spec.Address - first.Address]);
        }
    }

    private async Task<object> WriteSpecAsync(McPointSpec spec, object value, CancellationToken cancellationToken)
    {
        if (!spec.Writable)
        {
            throw new ZeusException($"点 {Name}.{spec.Name} 未标为可写。");
        }

        if (spec.IsBit)
        {
            if (spec.DeviceCode == McDeviceCode.InputRelay)
            {
                throw new ZeusException($"点 {Name}.{spec.Name} 位于 X 输入继电器，不能写回。");
            }

            var bit = ConvertToBoolean(value, spec.Name);
            await WriteBitsAsync(spec.DeviceCode, spec.Address, [bit], cancellationToken).ConfigureAwait(false);
            return bit;
        }

        if (!spec.IsWord)
        {
            throw new ZeusException($"点 {Name}.{spec.Name} 使用的 MC 软元件不支持点表写回。");
        }

        var raw = ConvertToWord(spec, value);
        await WriteWordsAsync(spec.DeviceCode, spec.Address, [raw], cancellationToken).ConfigureAwait(false);
        return spec.Convert is null ? raw : spec.Convert(raw);
    }

    private McPointSpec FindSpec(string pointName)
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

    private ushort ConvertToWord(McPointSpec spec, object value)
    {
        if (spec.Scale is { } scale)
        {
            var engineering = ConvertToDouble(value, spec.Name);
            var raw = engineering / scale;
            if (!double.IsFinite(raw))
            {
                throw new ZeusException($"点 {Name}.{spec.Name} 的工程值 {engineering} 无法按 scale={scale} 反算。");
            }

            var rounded = Math.Round(raw, MidpointRounding.AwayFromZero);
            if (rounded is < ushort.MinValue or > ushort.MaxValue)
            {
                throw new ZeusException(
                    $"点 {Name}.{spec.Name} 的工程值 {engineering} 反算为 {rounded}，超出 MC 字软元件 0–65535。");
            }

            return (ushort)rounded;
        }

        return ConvertToUInt16(value, spec.Name);
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

    private static double ConvertToDouble(object value, string pointName)
    {
        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new ZeusException($"点 {pointName} 需要数值，无法把 {value}（{value.GetType().Name}）写回。", ex);
        }
    }

    private static ushort ConvertToUInt16(object value, string pointName)
    {
        try
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var rounded = Math.Round(number, MidpointRounding.AwayFromZero);
            if (rounded is < ushort.MinValue or > ushort.MaxValue)
            {
                throw new ZeusException($"点 {pointName} 的值 {value} 超出 MC 字软元件 0–65535。");
            }

            return (ushort)rounded;
        }
        catch (ZeusException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ZeusException($"点 {pointName} 需要 0–65535 的整数，无法把 {value}（{value.GetType().Name}）写回。", ex);
        }
    }

    private List<List<McPointSpec>> GroupConsecutive(IReadOnlyList<McPointSpec> specs)
    {
        var groups = new List<List<McPointSpec>>();
        foreach (var spec in specs.OrderBy(item => item.DeviceCode).ThenBy(item => item.Address))
        {
            if (groups.Count > 0)
            {
                var current = groups[^1];
                var last = current[^1];
                if (last.DeviceCode == spec.DeviceCode
                    && spec.Address == last.Address + 1
                    && current.Count < GetMaxPoints(spec))
                {
                    current.Add(spec);
                    continue;
                }
            }

            groups.Add([spec]);
        }

        return groups;
    }

    private int GetMaxPoints(McPointSpec spec)
        => spec.IsWord ? GetMaxWordPoints() : GetMaxBitPoints();

    private int GetMaxWordPoints()
        => _options.FrameType == McFrameType.Frame1E ? 256 : 960;

    private int GetMaxBitPoints()
        => _options.FrameType == McFrameType.Frame1E
            ? 256
            : _options.DataEncoding == McDataEncoding.Ascii ? 3584 : 7168;

    private static void EnsurePointMapSupported(Mc3EOptions options, IReadOnlyList<McPointSpec> specs)
    {
        if (options.FrameType != McFrameType.Frame1E)
        {
            return;
        }

        foreach (var spec in specs)
        {
            if (spec.DeviceCode == McDeviceCode.ExtendedFileRegister)
            {
                throw new ZeusException($"MC 1E 帧不支持 ZR 扩展文件寄存器点 {spec.Name}。请改用 3E/4E，或移除该点。");
            }
        }
    }
}
