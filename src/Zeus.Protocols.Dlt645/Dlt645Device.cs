using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 面向业务的 DL/T 645-2007 表计设备：绑定通道与表地址，暴露常用数据项读写。
/// 声明了点表后实现 <see cref="IAcquisitionSource"/>，由宿主采集循环自动轮询；
/// 标为可写的点实现 <see cref="IPointWriter"/>，可通过点表按名称下发。
/// </summary>
public sealed class Dlt645Device : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly Dlt645Client _client;
    private readonly IReadOnlyList<Dlt645PointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>创建设备。通常由 <c>AddDlt645</c> 构造。</summary>
    public Dlt645Device(
        string name,
        IChannel channel,
        Dlt645Options? options = null,
        TimeSpan? timeout = null,
        Dlt645PointMap? pointMap = null,
        ILogger<Dlt645Device>? logger = null)
        : base(name, channel, logger)
    {
        _client = new Dlt645Client(channel, options, timeout);
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs
            .Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable))
            .ToArray();
    }

    /// <summary>底层 DL/T 645 客户端。</summary>
    public Dlt645Client Client => _client;

    /// <inheritdoc />
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <summary>读取数据项，返回不含数据项标识的原始数据区。</summary>
    public Task<byte[]> ReadDataAsync(uint dataIdentifier, CancellationToken cancellationToken = default)
        => _client.ReadDataAsync(dataIdentifier, cancellationToken);

    /// <summary>写入数据项。</summary>
    public Task WriteDataAsync(uint dataIdentifier, IReadOnlyList<byte> data, CancellationToken cancellationToken = default)
        => _client.WriteDataAsync(dataIdentifier, data, cancellationToken: cancellationToken);

    /// <summary>读取 BCD 数值数据项。</summary>
    public Task<double> ReadBcdAsync(uint dataIdentifier, int byteLength, double scale, CancellationToken cancellationToken = default)
        => _client.ReadBcdAsync(dataIdentifier, byteLength, scale, cancellationToken);

    /// <summary>写入 BCD 数值数据项。</summary>
    public Task WriteBcdAsync(uint dataIdentifier, double value, int byteLength, double scale, CancellationToken cancellationToken = default)
        => _client.WriteBcdAsync(dataIdentifier, value, byteLength, scale, cancellationToken);

    /// <inheritdoc />
    public async Task PollAsync(IPointTableWriter table, CancellationToken cancellationToken = default)
    {
        foreach (var spec in _specs)
        {
            var qualified = Name + "." + spec.Name;
            try
            {
                var data = await ReadDataAsync(spec.DataIdentifier, cancellationToken).ConfigureAwait(false);
                table.Publish(qualified, DecodeSpec(spec, data));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogAcquisitionFailed(ex, spec.Name);
                table.PublishError(qualified, ex.Message);
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
            if (spec.DataType == Dlt645DataType.Bcd)
            {
                var number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                await WriteBcdAsync(spec.DataIdentifier, number, spec.DataLength, spec.Scale, cancellationToken).ConfigureAwait(false);
                published = Dlt645Codec.DecodeBcd(Dlt645Codec.EncodeBcd(number, spec.DataLength, spec.Scale), spec.Scale);
            }
            else
            {
                var bytes = ConvertToBytes(value, spec.Name);
                if (bytes.Length != spec.DataLength)
                {
                    throw new ZeusException($"点 {qualified} 需要 {spec.DataLength} 字节，实际写入 {bytes.Length} 字节。");
                }

                await WriteDataAsync(spec.DataIdentifier, bytes, cancellationToken).ConfigureAwait(false);
                published = bytes;
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

    private static object DecodeSpec(Dlt645PointSpec spec, IReadOnlyList<byte> data)
    {
        if (data.Count < spec.DataLength)
        {
            throw new ZeusProtocolException(
                $"DL/T 645 数据项 {Dlt645Codec.FormatDataIdentifier(spec.DataIdentifier)} 返回 {data.Count} 字节，少于期望的 {spec.DataLength} 字节。");
        }

        var payload = data.Take(spec.DataLength).ToArray();
        return spec.DataType == Dlt645DataType.Bcd
            ? Dlt645Codec.DecodeBcd(payload, spec.Scale)
            : payload;
    }

    private Dlt645PointSpec FindSpec(string pointName)
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

    private static byte[] ConvertToBytes(object value, string pointName)
    {
        if (value is byte[] bytes)
        {
            return bytes;
        }

        if (value is ReadOnlyMemory<byte> memory)
        {
            return memory.ToArray();
        }

        if (value is Memory<byte> writableMemory)
        {
            return writableMemory.ToArray();
        }

        if (value is IEnumerable<byte> enumerable)
        {
            return enumerable.ToArray();
        }

        if (value is string text)
        {
            return Dlt645Codec.ParseHexData(text);
        }

        throw new ZeusException($"点 {pointName} 需要 byte[] 或十六进制字符串，无法把 {value.GetType().Name} 写回。");
    }
}
