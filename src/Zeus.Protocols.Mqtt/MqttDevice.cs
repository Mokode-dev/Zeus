using System.Globalization;
using System.Text;

namespace Zeus;

/// <summary>把 MQTT 主题消息映射为 Zeus 点表的设备。</summary>
public sealed class MqttDevice : DeviceBase, IAcquisitionSource, IPointWriter, IAsyncDisposable
{
    private readonly MqttClient _client;
    private readonly IReadOnlyList<MqttPointSpec> _specs;
    private readonly IReadOnlyList<PointDefinition> _points;
    private readonly Dictionary<string, byte[]> _latest = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private bool _subscribed;

    /// <summary>创建 MQTT 设备。</summary>
    public MqttDevice(
        string name,
        IChannel channel,
        MqttOptions? options = null,
        TimeSpan? timeout = null,
        MqttPointMap? pointMap = null)
        : base(name, channel)
    {
        _client = new MqttClient(channel, options, timeout, name + "-client");
        _specs = pointMap?.Points.ToArray() ?? [];
        _points = _specs.Select(spec => new PointDefinition(spec.Name, Name, spec.Kind, spec.AlarmLimits, spec.Writable)).ToArray();
        _client.MessageReceived += OnMessageReceived;
        _client.Channel.StateChanged += OnChannelStateChanged;
    }

    /// <summary>底层 MQTT 客户端。</summary>
    public MqttClient Client => _client;

    /// <summary>设备点表定义。</summary>
    public IReadOnlyList<PointDefinition> Points => _points;

    /// <summary>连接并订阅点表中声明的所有主题。</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSubscriptionsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>发布一条 UTF-8 文本消息。</summary>
    public Task PublishTextAsync(string topic, string payload, bool retain = false, CancellationToken cancellationToken = default)
        => _client.PublishAsync(topic, Encoding.UTF8.GetBytes(payload ?? string.Empty), retain, cancellationToken);

    /// <summary>按指定 QoS 发布一条 UTF-8 文本消息。</summary>
    public Task PublishTextAsync(
        string topic,
        string payload,
        MqttQualityOfService qualityOfService,
        bool retain = false,
        CancellationToken cancellationToken = default)
        => _client.PublishAsync(topic, Encoding.UTF8.GetBytes(payload ?? string.Empty), qualityOfService, retain, cancellationToken);

    /// <inheritdoc />
    public async Task PollAsync(IPointTableWriter table, CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        _client.DrainMessages();
        foreach (var spec in _specs)
        {
            var qualified = Name + "." + spec.Name;
            byte[]? payload;
            lock (_sync)
            {
                _latest.TryGetValue(spec.Topic, out payload);
            }

            if (payload is null)
            {
                continue;
            }

            try
            {
                table.Publish(qualified, DecodeValue(spec.DataType, payload));
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

            var payload = EncodeValue(spec.DataType, value, spec.Name);
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
            await _client.PublishAsync(spec.Topic, payload, spec.QualityOfService, spec.Retain, cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _latest[spec.Topic] = payload.ToArray();
            }

            table.Publish(qualified, DecodeValue(spec.DataType, payload));
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
    public async ValueTask DisposeAsync()
    {
        _client.MessageReceived -= OnMessageReceived;
        _client.Channel.StateChanged -= OnChannelStateChanged;
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private async Task EnsureSubscriptionsAsync(CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            return;
        }

        foreach (var spec in _specs)
        {
            await _client.SubscribeAsync(spec.Topic, spec.QualityOfService, cancellationToken).ConfigureAwait(false);
        }

        _subscribed = true;
    }

    private void OnMessageReceived(object? sender, MqttMessage message)
    {
        lock (_sync)
        {
            _latest[message.Topic] = message.Payload.ToArray();
        }
    }

    private void OnChannelStateChanged(object? sender, ChannelStateChangedEventArgs e)
    {
        if (e.Current is ChannelState.Closed or ChannelState.Faulted)
        {
            _subscribed = false;
        }
    }

    private MqttPointSpec FindSpec(string pointName)
    {
        if (string.IsNullOrWhiteSpace(pointName))
        {
            throw new ZeusException("写回点名不能为空。");
        }

        var key = pointName.Trim();
        return _specs.FirstOrDefault(spec => string.Equals(spec.Name, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ZeusException($"设备 {Name} 上找不到点 {key}。");
    }

    private static object DecodeValue(MqttDataType dataType, ReadOnlySpan<byte> payload)
    {
        var text = Encoding.UTF8.GetString(payload);
        return dataType switch
        {
            MqttDataType.Text => text,
            MqttDataType.Boolean => ParseBoolean(text),
            MqttDataType.Int32 => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            MqttDataType.Int64 => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            MqttDataType.Double => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
            MqttDataType.Bytes => payload.ToArray(),
            _ => throw new MqttException($"不支持的 MQTT 点类型 {dataType}。")
        };
    }

    private static byte[] EncodeValue(MqttDataType dataType, object value, string pointName)
    {
        if (dataType == MqttDataType.Bytes && value is byte[] bytes)
        {
            return bytes.ToArray();
        }

        var text = dataType switch
        {
            MqttDataType.Text => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            MqttDataType.Boolean => ConvertBoolean(value, pointName) ? "true" : "false",
            MqttDataType.Int32 => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            MqttDataType.Int64 => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            MqttDataType.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            _ => throw new ZeusException($"点 {pointName} 需要 byte[] 值。")
        };
        return Encoding.UTF8.GetBytes(text);
    }

    private static bool ParseBoolean(string value)
        => value.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ when bool.TryParse(value, out var result) => result,
            _ => throw new MqttException($"MQTT 布尔载荷 {value} 无效，期望 true/false 或 1/0。")
        };

    private static bool ConvertBoolean(object value, string pointName)
    {
        if (value is bool bit)
        {
            return bit;
        }

        if (value is string text)
        {
            return ParseBoolean(text);
        }

        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new ZeusException($"点 {pointName} 需要布尔值。", ex);
        }
    }
}
