namespace Zeus;

/// <summary>
/// 面向业务的 Siemens S7 设备：绑定通道与 rack/slot，暴露 DB、I、Q、M 区读写。
/// 声明了点表后实现 <see cref="IAcquisitionSource"/>，由宿主采集循环自动轮询；
/// 标为可写的点实现 <see cref="IPointWriter"/>，可通过点表按名称下发。
/// </summary>
public sealed class S7Device : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly S7Client _client;
    private readonly IReadOnlyList<S7PointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>
    /// 创建 S7 设备。通常由 <c>AddSiemensS7</c> 构造。
    /// </summary>
    /// <param name="name">设备名。</param>
    /// <param name="channel">传输通道。</param>
    /// <param name="options">S7 会话选项。</param>
    /// <param name="timeout">应答超时。</param>
    /// <param name="pointMap">可选点表。为 <c>null</c> 或不含点时不参与周期采集。</param>
    public S7Device(
        string name,
        IChannel channel,
        S7Options? options = null,
        TimeSpan? timeout = null,
        S7PointMap? pointMap = null)
        : base(name, channel)
    {
        _client = new S7Client(channel, options, timeout);
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs
            .Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable))
            .ToArray();
    }

    /// <summary>底层 S7 客户端。</summary>
    public S7Client Client => _client;

    /// <inheritdoc />
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <summary>读取 DB 区连续字节。</summary>
    public Task<byte[]> ReadDataBlockBytesAsync(int dbNumber, int byteOffset, ushort length, CancellationToken cancellationToken = default)
        => _client.ReadBytesAsync(S7Area.DataBlock, byteOffset, length, dbNumber, cancellationToken);

    /// <summary>写入 DB 区连续字节。</summary>
    public Task WriteDataBlockBytesAsync(int dbNumber, int byteOffset, IReadOnlyList<byte> values, CancellationToken cancellationToken = default)
        => _client.WriteBytesAsync(S7Area.DataBlock, byteOffset, values, dbNumber, cancellationToken);

    /// <summary>读取 M 区连续字节。</summary>
    public Task<byte[]> ReadMarkerBytesAsync(int byteOffset, ushort length, CancellationToken cancellationToken = default)
        => _client.ReadBytesAsync(S7Area.Merkers, byteOffset, length, cancellationToken: cancellationToken);

    /// <summary>写入 M 区连续字节。</summary>
    public Task WriteMarkerBytesAsync(int byteOffset, IReadOnlyList<byte> values, CancellationToken cancellationToken = default)
        => _client.WriteBytesAsync(S7Area.Merkers, byteOffset, values, cancellationToken: cancellationToken);

    /// <summary>读取一个 Bool 点。</summary>
    public Task<bool> ReadBoolAsync(S7Area area, int byteOffset, int bitOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.ReadBoolAsync(area, byteOffset, bitOffset, dbNumber, cancellationToken);

    /// <summary>写入一个 Bool 点。</summary>
    public Task WriteBoolAsync(S7Area area, int byteOffset, int bitOffset, bool value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.WriteBoolAsync(area, byteOffset, bitOffset, value, dbNumber, cancellationToken);

    /// <summary>读取一个 Byte 点。</summary>
    public Task<byte> ReadByteAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.ReadByteAsync(area, byteOffset, dbNumber, cancellationToken);

    /// <summary>写入一个 Byte 点。</summary>
    public Task WriteByteAsync(S7Area area, int byteOffset, byte value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.WriteByteAsync(area, byteOffset, value, dbNumber, cancellationToken);

    /// <summary>读取一个 Word 点。</summary>
    public Task<ushort> ReadWordAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.ReadWordAsync(area, byteOffset, dbNumber, cancellationToken);

    /// <summary>写入一个 Word 点。</summary>
    public Task WriteWordAsync(S7Area area, int byteOffset, ushort value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.WriteWordAsync(area, byteOffset, value, dbNumber, cancellationToken);

    /// <summary>读取一个 DWord 点。</summary>
    public Task<uint> ReadDWordAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.ReadDWordAsync(area, byteOffset, dbNumber, cancellationToken);

    /// <summary>写入一个 DWord 点。</summary>
    public Task WriteDWordAsync(S7Area area, int byteOffset, uint value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.WriteDWordAsync(area, byteOffset, value, dbNumber, cancellationToken);

    /// <summary>读取一个 Int 点。</summary>
    public Task<short> ReadIntAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.ReadIntAsync(area, byteOffset, dbNumber, cancellationToken);

    /// <summary>写入一个 Int 点。</summary>
    public Task WriteIntAsync(S7Area area, int byteOffset, short value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.WriteIntAsync(area, byteOffset, value, dbNumber, cancellationToken);

    /// <summary>读取一个 DInt 点。</summary>
    public Task<int> ReadDIntAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.ReadDIntAsync(area, byteOffset, dbNumber, cancellationToken);

    /// <summary>写入一个 DInt 点。</summary>
    public Task WriteDIntAsync(S7Area area, int byteOffset, int value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.WriteDIntAsync(area, byteOffset, value, dbNumber, cancellationToken);

    /// <summary>读取一个 Real 点。</summary>
    public Task<float> ReadRealAsync(S7Area area, int byteOffset, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.ReadRealAsync(area, byteOffset, dbNumber, cancellationToken);

    /// <summary>写入一个 Real 点。</summary>
    public Task WriteRealAsync(S7Area area, int byteOffset, float value, int dbNumber = 0, CancellationToken cancellationToken = default)
        => _client.WriteRealAsync(area, byteOffset, value, dbNumber, cancellationToken);

    /// <inheritdoc />
    public async Task PollAsync(IPointTableWriter table, CancellationToken cancellationToken = default)
    {
        foreach (var spec in _specs)
        {
            try
            {
                var raw = await _client.ReadAreaAsync(
                        spec.Area,
                        spec.DbNumber,
                        spec.ByteOffset,
                        spec.BitOffset,
                        spec.DataType,
                        spec.ByteLength,
                        cancellationToken)
                    .ConfigureAwait(false);
                table.Publish(Name + "." + spec.Name, S7Codec.DecodeValue(spec.DataType, raw, spec.Scale));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                table.PublishError(Name + "." + spec.Name, ex.Message);
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

            if (spec.Area == S7Area.Inputs)
            {
                throw new ZeusException($"点 {qualified} 位于 S7 输入区 I，不能写回。");
            }

            var raw = S7Codec.EncodeValue(spec.DataType, value, spec.Scale);
            await _client.WriteAreaAsync(
                    spec.Area,
                    spec.DbNumber,
                    spec.ByteOffset,
                    spec.BitOffset,
                    spec.DataType,
                    raw,
                    cancellationToken)
                .ConfigureAwait(false);
            table.Publish(qualified, S7Codec.DecodeValue(spec.DataType, raw, spec.Scale));
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

    private S7PointSpec FindSpec(string pointName)
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
}
