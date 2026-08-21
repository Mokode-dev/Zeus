namespace Zeus;

/// <summary>
/// JSON 配置文本规范化。协议绑定与装载器共用，避免各包复制一份大小写/下划线处理。
/// </summary>
public static class ZeusConfigurationText
{
    /// <summary>
    /// 去掉空白、转小写，并把下划线换成连字符。
    /// </summary>
    public static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-");

    /// <summary>
    /// 校验名称非空。
    /// </summary>
    public static void EnsureName(string? name, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException($"{path}.name 不能为空。");
        }
    }

    /// <summary>
    /// 校验报警限为有限数值。
    /// </summary>
    public static void ValidateAlarmLimit(double? value, string path)
    {
        if (value is { } number && !double.IsFinite(number))
        {
            throw new ZeusException($"{path} 必须是有限数值。");
        }
    }

    /// <summary>
    /// 校验点上的报警限对与回差。
    /// </summary>
    public static void ValidatePointAlarms(PointConfiguration point, string path)
    {
        ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
        ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
        if (point.LowAlarmLimit > point.HighAlarmLimit)
        {
            throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
        }

        if (point.Deadband < 0 || !double.IsFinite(point.Deadband))
        {
            throw new ZeusException($"{path}.deadband 必须是大于或等于 0 的有限数值。");
        }
    }

    /// <summary>
    /// 由 JSON 点生成报警限；未配置阈值时返回 <c>null</c>。
    /// </summary>
    public static PointAlarmLimits? CreateAlarmLimits(PointConfiguration point)
        => point.LowAlarmLimit is not null || point.HighAlarmLimit is not null
            ? new PointAlarmLimits(point.LowAlarmLimit, point.HighAlarmLimit, point.Deadband)
            : null;

    /// <summary>
    /// 点表指纹，供热更新判断设备是否需要重建。
    /// </summary>
    public static string PointFingerprint(PointConfiguration point)
        => string.Join(':',
            point.Name,
            Normalize(point.Table),
            Normalize(point.DeviceCode),
            Normalize(point.Area),
            Normalize(point.Tag),
            point.Topic,
            point.Oid,
            Normalize(point.MqttQos),
            point.MqttRetain,
            Normalize(point.DataType),
            point.DataLength,
            point.DbNumber,
            point.Address,
            point.BitOffset,
            point.Scale,
            point.Signed,
            point.LowAlarmLimit,
            point.HighAlarmLimit,
            point.Deadband,
            point.Writable);
}
