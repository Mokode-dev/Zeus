using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 面向业务的 Allen-Bradley EtherNet/IP 设备：绑定通道，暴露 CIP 对象访问与标签读写。
/// 声明了点表后实现 <see cref="IAcquisitionSource"/>，由宿主采集循环自动轮询；
/// 标为可写的点实现 <see cref="IPointWriter"/>，可通过点表按名称下发。
/// </summary>
public sealed class EtherNetIpDevice : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly EtherNetIpClient _client;
    private readonly IReadOnlyList<EtherNetIpPointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>创建设备。通常由 <c>AddAllenBradleyEtherNetIp</c> 构造。</summary>
    public EtherNetIpDevice(
        string name,
        IChannel channel,
        EtherNetIpOptions? options = null,
        TimeSpan? timeout = null,
        EtherNetIpPointMap? pointMap = null,
        ILogger<EtherNetIpDevice>? logger = null)
        : base(name, channel, logger)
    {
        _client = new EtherNetIpClient(channel, options, timeout);
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs
            .Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable))
            .ToArray();
    }

    /// <summary>底层 EtherNet/IP 客户端。</summary>
    public EtherNetIpClient Client => _client;

    /// <inheritdoc />
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <summary>执行任意 CIP 服务，返回已去掉 CIP 状态头的数据区。</summary>
    public Task<byte[]> ExecuteCipAsync(byte service, ReadOnlyMemory<byte> path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => _client.ExecuteCipAsync(service, path, data, cancellationToken);

    /// <summary>读取 CIP 对象单个属性。</summary>
    public Task<byte[]> GetAttributeSingleAsync(ushort classId, uint instanceId, ushort attributeId, CancellationToken cancellationToken = default)
        => _client.GetAttributeSingleAsync(classId, instanceId, attributeId, cancellationToken);

    /// <summary>写入 CIP 对象单个属性。</summary>
    public Task SetAttributeSingleAsync(ushort classId, uint instanceId, ushort attributeId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => _client.SetAttributeSingleAsync(classId, instanceId, attributeId, data, cancellationToken);

    /// <summary>读取 Allen-Bradley 符号标签。</summary>
    public Task<object> ReadTagAsync(string tagName, EtherNetIpDataType dataType, ushort elementCount = 1, double? scale = null, CancellationToken cancellationToken = default)
        => _client.ReadTagAsync(tagName, dataType, elementCount, scale, cancellationToken);

    /// <summary>写入 Allen-Bradley 符号标签。</summary>
    public Task WriteTagAsync(string tagName, EtherNetIpDataType dataType, object value, double? scale = null, CancellationToken cancellationToken = default)
        => _client.WriteTagAsync(tagName, dataType, value, scale, cancellationToken);

    /// <inheritdoc />
    public async Task PollAsync(IPointTableWriter table, CancellationToken cancellationToken = default)
    {
        foreach (var spec in _specs)
        {
            var qualified = Name + "." + spec.Name;
            try
            {
                var value = await ReadTagAsync(spec.TagName, spec.DataType, scale: spec.Scale, cancellationToken: cancellationToken).ConfigureAwait(false);
                table.Publish(qualified, value);
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

            var outbound = spec.DataType == EtherNetIpDataType.Bool ? ConvertToBoolean(value, spec.Name) : value;
            await WriteTagAsync(spec.TagName, spec.DataType, outbound, spec.Scale, cancellationToken).ConfigureAwait(false);
            var published = await ReadTagAsync(spec.TagName, spec.DataType, scale: spec.Scale, cancellationToken: cancellationToken).ConfigureAwait(false);
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

    private EtherNetIpPointSpec FindSpec(string pointName)
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
}
