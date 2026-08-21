using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zeus;

/// <summary>
/// 读取并校验 Zeus JSON 工程配置。错误消息面向现场工程师，指出文件路径与字段名。
/// 设备与虚拟从站的协议细节由 <see cref="IZeusJsonBinder"/> 处理，本类型只校验通道拓扑与采集选项。
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
    /// 校验必填项、名称唯一性与通道引用。协议字段交给已登记的 JSON 绑定。
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

        if (document.Reconnect.InitialDelayMilliseconds < 0)
        {
            throw new ZeusException($"{sourceName} 中 reconnect.initialDelayMilliseconds 不能为负数。");
        }

        if (document.Reconnect.MaxDelayMilliseconds < 0)
        {
            throw new ZeusException($"{sourceName} 中 reconnect.maxDelayMilliseconds 不能为负数。");
        }

        if (document.Reconnect.BackoffMultiplier < 1)
        {
            throw new ZeusException($"{sourceName} 中 reconnect.backoffMultiplier 必须大于或等于 1。");
        }

        if (document.Acquisition.SourceTimeoutMilliseconds < 0)
        {
            throw new ZeusException($"{sourceName} 中 acquisition.sourceTimeoutMilliseconds 不能为负数。");
        }

        var channelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < document.Channels.Count; i++)
        {
            var channel = document.Channels[i];
            var path = $"{sourceName} channels[{i}]";
            ZeusConfigurationText.EnsureName(channel.Name, path);
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
                    ValidateNetworkChannel(channel, path, "tcp");
                    break;
                case "udp":
                    ValidateNetworkChannel(channel, path, "udp");
                    if (channel.LocalPort is < 0 or > 65535)
                    {
                        throw new ZeusException($"{path}.localPort 必须介于 0 与 65535 之间，0 表示自动分配。");
                    }

                    break;
                case "tcp-server":
                    ValidateTcpServerChannel(channel, path);
                    break;
                case "udp-server":
                    ValidateUdpServerChannel(channel, path);
                    break;
                default:
                    throw new ZeusException(
                        $"{path}.type「{channel.Type}」不受支持。可选 virtual、serial、tcp、tcp-server、udp、udp-server。");
            }
        }

        var deviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < document.Devices.Count; i++)
        {
            var device = document.Devices[i];
            var path = $"{sourceName} devices[{i}]";
            ZeusConfigurationText.EnsureName(device.Name, path);
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
            var binder = ZeusJsonBinders.FindDevice(type);
            if (binder is null)
            {
                var known = string.Join("、", ZeusJsonBinders.All.SelectMany(item => item.DeviceTypes).Distinct());
                throw new ZeusException(
                    $"{path}.type「{device.Type}」没有对应的 JSON 绑定。请引用对应协议包（当前已加载：{(string.IsNullOrEmpty(known) ? "无" : known)}）。");
            }

            binder.ValidateDevice(device, path);
        }
    }

    /// <summary>规范化类型字符串，供绑定查找与指纹使用。</summary>
    public static string Normalize(string? value) => ZeusConfigurationText.Normalize(value);

    private static void ValidateVirtual(ChannelConfiguration channel, string path)
    {
        if (string.IsNullOrWhiteSpace(channel.Responder))
        {
            return;
        }

        var responder = Normalize(channel.Responder);
        var binder = ZeusJsonBinders.FindResponder(responder);
        if (binder is null)
        {
            var known = string.Join("、", ZeusJsonBinders.All.SelectMany(item => item.ResponderTypes).Distinct());
            throw new ZeusException(
                $"{path}.responder「{channel.Responder}」没有对应的 JSON 绑定。请引用对应协议包，或省略 responder 以回显写入。当前已加载：{(string.IsNullOrEmpty(known) ? "无" : known)}。");
        }

        binder.ValidateResponder(channel, path);
    }

    private static void ValidateNetworkChannel(ChannelConfiguration channel, string path, string type)
    {
        if (string.IsNullOrWhiteSpace(channel.Host))
        {
            throw new ZeusException($"{path} 类型为 {type} 时必须提供 host。");
        }

        if (channel.Port is <= 0 or > 65535)
        {
            throw new ZeusException($"{path}.port 必须介于 1 与 65535 之间。");
        }
    }

    private static void ValidateUdpServerChannel(ChannelConfiguration channel, string path)
    {
        if (!string.IsNullOrWhiteSpace(channel.LocalAddress)
            && !IPAddress.TryParse(channel.LocalAddress.Trim(), out _))
        {
            throw new ZeusException($"{path}.localAddress 必须是有效 IP 地址，例如 0.0.0.0 或 127.0.0.1。");
        }

        if (channel.LocalPort is < 0 or > 65535)
        {
            throw new ZeusException($"{path}.localPort 必须介于 0 与 65535 之间，0 表示自动分配。");
        }

        if (channel.Port is < 0 or > 65535)
        {
            throw new ZeusException($"{path}.port 必须介于 0 与 65535 之间；udp-server 未提供 localPort 时会把 port 当作监听端口。");
        }
    }

    private static void ValidateTcpServerChannel(ChannelConfiguration channel, string path)
    {
        if (!string.IsNullOrWhiteSpace(channel.LocalAddress)
            && !IPAddress.TryParse(channel.LocalAddress.Trim(), out _))
        {
            throw new ZeusException($"{path}.localAddress 必须是有效 IP 地址，例如 0.0.0.0 或 127.0.0.1。");
        }

        if (channel.LocalPort is < 0 or > 65535)
        {
            throw new ZeusException($"{path}.localPort 必须介于 0 与 65535 之间，0 表示自动分配。");
        }

        if (channel.Port is < 0 or > 65535)
        {
            throw new ZeusException($"{path}.port 必须介于 0 与 65535 之间；tcp-server 未提供 localPort 时会把 port 当作监听端口。");
        }
    }
}
