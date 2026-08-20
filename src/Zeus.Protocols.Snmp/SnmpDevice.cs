using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>把 SNMP OID 映射为 Zeus 点表的设备。</summary>
public sealed class SnmpDevice : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly SnmpClient _client;
    private readonly IReadOnlyList<SnmpPointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>创建 SNMP 设备。</summary>
    public SnmpDevice(
        string name,
        IChannel channel,
        SnmpOptions? options = null,
        TimeSpan? timeout = null,
        SnmpPointMap? pointMap = null,
        ILogger<SnmpDevice>? logger = null)
        : base(name, channel, logger)
    {
        _client = new SnmpClient(channel, options, timeout);
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs.Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable)).ToArray();
    }

    /// <summary>底层 SNMP 客户端。</summary>
    public SnmpClient Client => _client;

    /// <summary>设备点表定义。</summary>
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <summary>读取一个 OID。</summary>
    public Task<SnmpValue> GetAsync(string oid, CancellationToken cancellationToken = default)
        => _client.GetAsync(oid, cancellationToken);

    /// <summary>写入一个 OID。</summary>
    public Task SetAsync(string oid, SnmpValue value, CancellationToken cancellationToken = default)
        => _client.SetAsync(oid, value, cancellationToken);

    /// <inheritdoc />
    public async Task PollAsync(IPointTableWriter table, CancellationToken cancellationToken = default)
    {
        foreach (var spec in _specs)
        {
            var qualified = Name + "." + spec.Name;
            try
            {
                var value = await _client.GetAsync(spec.Oid, cancellationToken).ConfigureAwait(false);
                table.Publish(qualified, SnmpCodec.ToEngineeringValue(SnmpCodec.Coerce(value, spec.DataType), spec.Scale));
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

            var wireValue = SnmpCodec.FromEngineeringValue(spec.DataType, value, spec.Scale);
            await _client.SetAsync(spec.Oid, wireValue, cancellationToken).ConfigureAwait(false);
            table.Publish(qualified, SnmpCodec.ToEngineeringValue(wireValue, spec.Scale));
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

    private SnmpPointSpec FindSpec(string pointName)
    {
        if (string.IsNullOrWhiteSpace(pointName))
        {
            throw new ZeusException("写回点名不能为空。");
        }

        var key = pointName.Trim();
        return _specs.FirstOrDefault(spec => string.Equals(spec.Name, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ZeusException($"设备 {Name} 上找不到点 {key}。");
    }
}
