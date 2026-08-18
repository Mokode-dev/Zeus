using System.Globalization;

namespace Zeus;

/// <summary>
/// 面向业务的 IEC 60870-5-104 站设备：绑定通道与公共地址，暴露总召唤采集与常用命令。
/// 声明了点表后实现 <see cref="IAcquisitionSource"/>，由宿主采集循环自动轮询；
/// 标为可写的点实现 <see cref="IPointWriter"/>，可通过点表按名称下发。
/// </summary>
public sealed class Iec104Device : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly Iec104Client _client;
    private readonly IReadOnlyList<Iec104PointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;

    /// <summary>创建设备。通常由 <c>AddIec104</c> 构造。</summary>
    public Iec104Device(
        string name,
        IChannel channel,
        Iec104Options? options = null,
        TimeSpan? timeout = null,
        Iec104PointMap? pointMap = null)
        : base(name, channel)
    {
        _client = new Iec104Client(channel, options, timeout);
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs
            .Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable))
            .ToArray();
    }

    /// <summary>底层 IEC104 客户端。</summary>
    public Iec104Client Client => _client;

    /// <inheritdoc />
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <summary>执行总召唤，返回内置信息对象。</summary>
    public Task<IReadOnlyList<Iec104InformationObject>> InterrogateAsync(CancellationToken cancellationToken = default)
        => _client.InterrogateAsync(cancellationToken);

    /// <summary>发送单点命令。</summary>
    public Task SendSingleCommandAsync(int address, bool command, CancellationToken cancellationToken = default)
        => _client.SendSingleCommandAsync(address, command, cancellationToken);

    /// <summary>发送归一化设点命令。线值范围为 -1 到 1。</summary>
    public Task SendNormalizedSetpointAsync(int address, double value, CancellationToken cancellationToken = default)
        => _client.SendNormalizedSetpointAsync(address, value, cancellationToken);

    /// <summary>发送标度化设点命令。</summary>
    public Task SendScaledSetpointAsync(int address, short value, CancellationToken cancellationToken = default)
        => _client.SendScaledSetpointAsync(address, value, cancellationToken);

    /// <summary>发送短浮点设点命令。</summary>
    public Task SendShortFloatSetpointAsync(int address, double value, CancellationToken cancellationToken = default)
        => _client.SendShortFloatSetpointAsync(address, value, cancellationToken);

    /// <inheritdoc />
    public async Task PollAsync(IPointTableWriter table, CancellationToken cancellationToken = default)
    {
        var values = await InterrogateAsync(cancellationToken).ConfigureAwait(false);
        foreach (var spec in _specs)
        {
            var qualified = Name + "." + spec.Name;
            try
            {
                var value = values.FirstOrDefault(item => item.Address == spec.Address && item.DataType == spec.DataType);
                if (value.Value is null)
                {
                    throw new ZeusProtocolException($"IEC104 总召唤未返回 IOA {spec.Address} 的 {spec.DataType} 值。");
                }

                table.Publish(qualified, DecodeSpec(spec, value.Value));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
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

            var wireValue = EncodeSpec(spec, value);
            switch (spec.DataType)
            {
                case Iec104DataType.SinglePoint:
                    await SendSingleCommandAsync(spec.Address, (bool)wireValue, cancellationToken).ConfigureAwait(false);
                    break;
                case Iec104DataType.Normalized:
                    await SendNormalizedSetpointAsync(spec.Address, (double)wireValue, cancellationToken).ConfigureAwait(false);
                    break;
                case Iec104DataType.Scaled:
                    await SendScaledSetpointAsync(spec.Address, (short)wireValue, cancellationToken).ConfigureAwait(false);
                    break;
                case Iec104DataType.ShortFloat:
                    await SendShortFloatSetpointAsync(spec.Address, (double)wireValue, cancellationToken).ConfigureAwait(false);
                    break;
            }

            table.Publish(qualified, DecodeSpec(spec, wireValue));
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

    private static object DecodeSpec(Iec104PointSpec spec, object value)
    {
        if (spec.DataType == Iec104DataType.SinglePoint)
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return spec.Scale is { } scale ? number * scale : value;
    }

    private static object EncodeSpec(Iec104PointSpec spec, object value)
    {
        if (spec.DataType == Iec104DataType.SinglePoint)
        {
            return ConvertToBoolean(value, spec.Name);
        }

        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (spec.Scale is { } scale)
        {
            number /= scale;
        }

        return spec.DataType switch
        {
            Iec104DataType.Normalized => (object)number,
            Iec104DataType.Scaled => Convert.ToInt16(Math.Round(number, MidpointRounding.AwayFromZero), CultureInfo.InvariantCulture),
            Iec104DataType.ShortFloat => (object)number,
            _ => throw new ZeusException($"IEC104 点 {spec.Name} 不支持写回 {spec.DataType}。")
        };
    }

    private Iec104PointSpec FindSpec(string pointName)
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
