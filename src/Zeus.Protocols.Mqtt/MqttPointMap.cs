namespace Zeus;

/// <summary>声明 MQTT 主题与 Zeus 点表之间的映射。</summary>
public sealed class MqttPointMap
{
    private readonly List<MqttPointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _topics = new(StringComparer.Ordinal);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<MqttPointSpec> Points => _points;

    /// <summary>声明 UTF-8 文本点。</summary>
    public MqttPointMap Text(string name, string topic)
        => Add(name, topic, MqttDataType.Text, PointValueKind.Object, null);

    /// <summary>声明布尔点。</summary>
    public MqttPointMap Boolean(string name, string topic)
        => Add(name, topic, MqttDataType.Boolean, PointValueKind.Boolean, null);

    /// <summary>声明 32 位整数点。</summary>
    public MqttPointMap Int32(string name, string topic, PointAlarmLimits? alarmLimits = null)
        => Add(name, topic, MqttDataType.Int32, PointValueKind.Object, alarmLimits);

    /// <summary>声明 64 位整数点。</summary>
    public MqttPointMap Int64(string name, string topic, PointAlarmLimits? alarmLimits = null)
        => Add(name, topic, MqttDataType.Int64, PointValueKind.Object, alarmLimits);

    /// <summary>声明双精度浮点点。</summary>
    public MqttPointMap Double(string name, string topic, PointAlarmLimits? alarmLimits = null)
        => Add(name, topic, MqttDataType.Double, PointValueKind.Double, alarmLimits);

    /// <summary>声明原始字节点。</summary>
    public MqttPointMap Bytes(string name, string topic)
        => Add(name, topic, MqttDataType.Bytes, PointValueKind.Object, null);

    /// <summary>把已声明的点标为可写。</summary>
    public MqttPointMap Writable(string name)
    {
        var index = FindIndex(name);
        _points[index] = _points[index] with { Writable = true };
        return this;
    }

    /// <summary>设置点的订阅与写回服务质量。</summary>
    public MqttPointMap WithQualityOfService(string name, MqttQualityOfService qualityOfService)
    {
        ValidateQualityOfService(qualityOfService);
        var index = FindIndex(name);
        _points[index] = _points[index] with { QualityOfService = qualityOfService };
        return this;
    }

    /// <summary>设置点写回时是否发布为保留消息。</summary>
    public MqttPointMap Retained(string name, bool retained = true)
    {
        var index = FindIndex(name);
        _points[index] = _points[index] with { Retain = retained };
        return this;
    }

    /// <summary>为已声明的数值点设置报警限。</summary>
    public MqttPointMap WithAlarmLimits(string name, double? low = null, double? high = null)
    {
        if (low > high)
        {
            throw new ZeusException($"MQTT 点 {name} 的低报警限不能高于高报警限。");
        }

        var index = FindIndex(name);
        var point = _points[index];
        if (point.DataType is MqttDataType.Text or MqttDataType.Boolean or MqttDataType.Bytes)
        {
            throw new ZeusException($"MQTT 点 {point.Name} 不是数值点，不能配置报警限。");
        }

        _points[index] = point with { AlarmLimits = new PointAlarmLimits(low, high) };
        return this;
    }

    private MqttPointMap Add(string name, string topic, MqttDataType dataType, PointValueKind kind, PointAlarmLimits? alarmLimits)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("MQTT 点名不能为空。");
        }

        if (string.IsNullOrWhiteSpace(topic) || topic.Contains('\0') || topic.Contains('+') || topic.Contains('#'))
        {
            throw new ZeusException("MQTT 点主题不能为空，也不能包含空字符、+ 或 # 通配符。");
        }

        if (alarmLimits?.Low > alarmLimits?.High)
        {
            throw new ZeusException($"MQTT 点 {name} 的低报警限不能高于高报警限。");
        }

        var normalizedName = name.Trim();
        var normalizedTopic = topic.Trim();
        if (!_names.Add(normalizedName))
        {
            throw new ZeusException($"同一台 MQTT 设备上点名 {normalizedName} 重复。");
        }

        if (!_topics.Add(normalizedTopic))
        {
            throw new ZeusException($"同一台 MQTT 设备上主题 {normalizedTopic} 重复。");
        }

        _points.Add(new MqttPointSpec(normalizedName, normalizedTopic, dataType, kind, alarmLimits, false));
        return this;
    }

    private int FindIndex(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("MQTT 点名不能为空。");
        }

        var normalized = name.Trim();
        for (var i = 0; i < _points.Count; i++)
        {
            if (string.Equals(_points[i].Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ZeusException($"找不到 MQTT 点 {normalized}，请先声明该点。");
    }

    private static void ValidateQualityOfService(MqttQualityOfService qualityOfService)
    {
        if (qualityOfService is < MqttQualityOfService.AtMostOnce or > MqttQualityOfService.ExactlyOnce)
        {
            throw new ZeusException($"MQTT QoS {(int)qualityOfService} 无效，可选 0、1、2。");
        }
    }
}
