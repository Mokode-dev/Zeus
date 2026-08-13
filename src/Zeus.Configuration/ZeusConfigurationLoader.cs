using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zeus;

/// <summary>
/// 读取并校验 Zeus JSON 工程配置。错误消息面向现场工程师，指出文件路径与字段名。
/// </summary>
public static class ZeusConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// 从磁盘读取配置。
    /// </summary>
    /// <param name="path">JSON 文件路径。</param>
    public static ZeusAppConfiguration LoadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ZeusException("配置文件路径不能为空。");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new ZeusException($"找不到配置文件 {fullPath}。请确认路径，或先从手册复制一份示例 JSON。");
        }

        string json;
        try
        {
            json = File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            throw new ZeusException($"无法读取配置文件 {fullPath}：{ex.Message}", ex);
        }

        return LoadJson(json, fullPath);
    }

    /// <summary>
    /// 从 JSON 文本读取配置。
    /// </summary>
    /// <param name="json">配置正文。</param>
    /// <param name="sourceName">用于错误消息的来源名，例如文件路径或「内存」。</param>
    public static ZeusAppConfiguration LoadJson(string json, string sourceName = "配置")
    {
        ZeusAppConfiguration? document;
        try
        {
            document = JsonSerializer.Deserialize<ZeusAppConfiguration>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ZeusException(
                $"{sourceName} 不是合法 JSON：{ex.Message} 请检查逗号、引号与注释是否使用 // 或 /* */。",
                ex);
        }

        if (document is null)
        {
            throw new ZeusException($"{sourceName} 解析结果为空。");
        }

        Validate(document, sourceName);
        return document;
    }

    /// <summary>
    /// 校验必填项、名称唯一性与通道引用。
    /// </summary>
    /// <param name="document">已反序列化的配置。</param>
    /// <param name="sourceName">来源名。</param>
    public static void Validate(ZeusAppConfiguration document, string sourceName = "配置")
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Acquisition.IntervalMilliseconds <= 0)
        {
            throw new ZeusException($"{sourceName} 中 acquisition.intervalMilliseconds 必须大于 0。");
        }

        var channelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < document.Channels.Count; i++)
        {
            var channel = document.Channels[i];
            var path = $"{sourceName} channels[{i}]";
            EnsureName(channel.Name, path);
            if (!channelNames.Add(channel.Name.Trim()))
            {
                throw new ZeusException($"{path}.name「{channel.Name}」重复。每个通道名在文件内必须唯一。");
            }

            switch (Normalize(channel.Type))
            {
                case "virtual":
                    ValidateVirtual(channel, path);
                    break;
                case "serial":
                    if (string.IsNullOrWhiteSpace(channel.PortName))
                    {
                        throw new ZeusException($"{path} 类型为 serial 时必须提供 portName，例如 COM3。");
                    }

                    if (channel.BaudRate <= 0)
                    {
                        throw new ZeusException($"{path}.baudRate 必须大于 0。");
                    }

                    break;
                case "tcp":
                    if (string.IsNullOrWhiteSpace(channel.Host))
                    {
                        throw new ZeusException($"{path} 类型为 tcp 时必须提供 host。");
                    }

                    if (channel.Port is <= 0 or > 65535)
                    {
                        throw new ZeusException($"{path}.port 必须介于 1 与 65535 之间。");
                    }

                    break;
                default:
                    throw new ZeusException(
                        $"{path}.type「{channel.Type}」不受支持。可选 virtual、serial、tcp。");
            }
        }

        var deviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < document.Devices.Count; i++)
        {
            var device = document.Devices[i];
            var path = $"{sourceName} devices[{i}]";
            EnsureName(device.Name, path);
            if (!deviceNames.Add(device.Name.Trim()))
            {
                throw new ZeusException($"{path}.name「{device.Name}」重复。");
            }

            if (string.IsNullOrWhiteSpace(device.Channel))
            {
                throw new ZeusException($"{path} 必须指定 channel。");
            }

            if (!channelNames.Contains(device.Channel.Trim()))
            {
                throw new ZeusException(
                    $"{path}.channel「{device.Channel}」未在 channels 中声明。请先写通道，再写设备。");
            }

            var type = Normalize(device.Type);
            if (type is not ("modbus-rtu" or "modbusrtu" or "rtu" or "modbus-tcp" or "modbustcp" or "tcp"))
            {
                throw new ZeusException($"{path}.type「{device.Type}」不受支持。可选 modbus-rtu、modbus-tcp。");
            }

            ValidatePoints(device.Points, path);
        }
    }

    private static void ValidateVirtual(ChannelConfiguration channel, string path)
    {
        if (string.IsNullOrWhiteSpace(channel.Responder))
        {
            return;
        }

        if (Normalize(channel.Responder) is not "modbus")
        {
            throw new ZeusException($"{path}.responder「{channel.Responder}」不受支持。当前仅支持 modbus，或省略以回显写入。");
        }

        var transport = Normalize(channel.Transport);
        if (transport is not ("rtu" or "tcp"))
        {
            throw new ZeusException($"{path}.transport「{channel.Transport}」不受支持。可选 rtu、tcp。");
        }
    }

    private static void ValidatePoints(List<PointConfiguration> points, string devicePath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var path = $"{devicePath}.points[{i}]";
            EnsureName(point.Name, path);
            if (!names.Add(point.Name.Trim()))
            {
                throw new ZeusException($"{path}.name「{point.Name}」在同一设备内重复。");
            }

            var table = Normalize(point.Table);
            if (table is not ("holding" or "holdingregister" or "input" or "inputregister" or "coil" or "discrete" or "discreteinput"))
            {
                throw new ZeusException($"{path}.table「{point.Table}」不受支持。可选 holding、input、coil、discrete。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }
        }
    }

    private static void EnsureName(string? name, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException($"{path}.name 不能为空。");
        }
    }

    internal static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-");
}
