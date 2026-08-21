using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;

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
            if (IsModbusDeviceType(type))
            {
                ValidatePoints(device.Points, path);
            }
            else if (IsFinsDeviceType(type))
            {
                ValidateFinsDevice(device, path);
            }
            else if (IsHostLinkDeviceType(type))
            {
                ValidateHostLinkDevice(device, path);
            }
            else if (IsMewtocolDeviceType(type))
            {
                ValidateMewtocolDevice(device, path);
            }
            else if (IsS7DeviceType(type))
            {
                ValidateS7Device(device, path);
            }
            else if (IsEtherNetIpDeviceType(type))
            {
                ValidateEtherNetIpDevice(device, path);
            }
            else if (IsDlt645DeviceType(type))
            {
                ValidateDlt645Device(device, path);
            }
            else if (IsIec104DeviceType(type))
            {
                ValidateIec104Device(device, path);
            }
            else if (IsMqttDeviceType(type))
            {
                ValidateMqttDevice(device, path);
            }
            else if (IsSnmpDeviceType(type))
            {
                ValidateSnmpDevice(device, path);
            }
            else if (IsMcDeviceType(type))
            {
                ValidateMcDevice(device, path);
            }
            else
            {
                throw new ZeusException($"{path}.type「{device.Type}」不受支持。可选 modbus-rtu、modbus-tcp、modbus-ascii、mitsubishi-mc、siemens-s7、omron-fins-udp、omron-fins-tcp、omron-host-link、panasonic-mewtocol、ethernet-ip、dlt645、iec104、mqtt、snmp。");
            }
        }
    }

    private static void ValidateVirtual(ChannelConfiguration channel, string path)
    {
        if (string.IsNullOrWhiteSpace(channel.Responder))
        {
            return;
        }

        var responder = Normalize(channel.Responder);
        if (responder is not ("modbus" or "mc" or "s7" or "fins" or "host-link" or "mewtocol" or "ethernet-ip" or "dlt645" or "iec104" or "mqtt" or "snmp"))
        {
            throw new ZeusException($"{path}.responder「{channel.Responder}」不受支持。当前支持 modbus、mc、s7、fins、host-link、mewtocol、ethernet-ip、dlt645、iec104、mqtt、snmp，或省略以回显写入。");
        }

        if (responder == "mqtt")
        {
            return;
        }

        if (responder == "snmp")
        {
            ValidateCommunity(channel.SnmpCommunity, $"{path}.snmpCommunity");
            if (channel.SnmpWriteCommunity is not null)
            {
                ValidateCommunity(channel.SnmpWriteCommunity, $"{path}.snmpWriteCommunity");
            }

            return;
        }

        if (responder == "dlt645")
        {
            ValidateDlt645Address(channel.MeterAddress, $"{path}.meterAddress");
            return;
        }

        if (responder == "iec104")
        {
            ValidateUInt16(channel.CommonAddress, $"{path}.commonAddress");
            return;
        }

        if (responder == "host-link")
        {
            if (channel.UnitId > 31)
            {
                throw new ZeusException($"{path}.unitId 必须介于 0 与 31 之间。Host Link 虚拟 PLC 使用两位十进制单元号。");
            }

            return;
        }

        if (responder == "mewtocol")
        {
            if (channel.UnitId is < 1 or > 99)
            {
                throw new ZeusException($"{path}.unitId 必须介于 1 与 99 之间。MEWTOCOL 虚拟 PLC 使用两位十进制站号。");
            }

            return;
        }

        if (responder is "mc" or "s7" or "ethernet-ip")
        {
            return;
        }

        var transport = Normalize(channel.Transport);
        if (responder == "fins")
        {
            if (transport is not ("udp" or "tcp"))
            {
                throw new ZeusException($"{path}.transport「{channel.Transport}」不受支持。FINS 虚拟从站可选 udp、tcp。");
            }

            return;
        }

        if (transport is not ("rtu" or "tcp" or "ascii"))
        {
            throw new ZeusException($"{path}.transport「{channel.Transport}」不受支持。可选 rtu、tcp、ascii。");
        }
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
            if (table is not ("holding" or "input" or "coil" or "discrete"))
            {
                throw new ZeusException($"{path}.table「{point.Table}」不受支持。可选 holding、input、coil、discrete。");
            }

            if (point.Address is < 0 or > ushort.MaxValue)
            {
                throw new ZeusException($"{path}.address 必须介于 0 与 65535 之间。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            if ((point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                && table is "coil" or "discrete")
            {
                throw new ZeusException($"{path} 是布尔点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
            }

            if (point.Writable && table is "input" or "discrete")
            {
                throw new ZeusException($"{path} 位于只读数据区，不能设置 writable: true。");
            }
        }
    }

    private static void ValidateMcDevice(DeviceConfiguration device, string path)
    {
        var frameType = ParseMcFrameType(device.FrameType, $"{path}.frameType");
        ParseMcDataEncoding(device.Encoding, $"{path}.encoding");
        ValidateByte(device.NetworkNumber, $"{path}.networkNumber");
        ValidateByte(device.PcNumber, $"{path}.pcNumber");
        ValidateUInt16(device.IoNumber, $"{path}.ioNumber");
        ValidateByte(device.StationNumber, $"{path}.stationNumber");
        ValidateUInt16(device.MonitoringTimer, $"{path}.monitoringTimer");
        ValidateUInt16(device.SerialNumber, $"{path}.serialNumber");

        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidateMcPoints(device.Points, path, frameType);
    }

    private static void ValidateS7Device(DeviceConfiguration device, string path)
    {
        ValidateByte(device.Rack, $"{path}.rack");
        if (device.Slot is < 0 or > 31)
        {
            throw new ZeusException($"{path}.slot 必须介于 0 与 31 之间。");
        }

        ValidateUInt16(device.LocalTsap, $"{path}.localTsap");
        if (device.RemoteTsap is { } remoteTsap)
        {
            ValidateUInt16(remoteTsap, $"{path}.remoteTsap");
        }

        if (device.RequestedPduLength is < 128 or > 960)
        {
            throw new ZeusException($"{path}.requestedPduLength 必须介于 128 与 960 之间。");
        }

        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidateS7Points(device.Points, path);
    }

    private static void ValidateFinsDevice(DeviceConfiguration device, string path)
    {
        ValidateByte(device.DestinationNetwork, $"{path}.destinationNetwork");
        ValidateByte(device.DestinationNode, $"{path}.destinationNode");
        ValidateByte(device.DestinationUnit, $"{path}.destinationUnit");
        ValidateByte(device.SourceNetwork, $"{path}.sourceNetwork");
        ValidateByte(device.SourceNode, $"{path}.sourceNode");
        ValidateByte(device.SourceUnit, $"{path}.sourceUnit");
        ValidateByte(device.GatewayCount, $"{path}.gatewayCount");
        ValidateByte(device.InformationControlField, $"{path}.informationControlField");
        ValidateByte(device.TcpRequestedClientNode, $"{path}.tcpRequestedClientNode");
        ParseFinsWordOrder(device.WordOrder, $"{path}.wordOrder");

        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidateFinsPoints(device.Points, path);
    }

    private static void ValidateHostLinkDevice(DeviceConfiguration device, string path)
    {
        if (device.UnitId > 31)
        {
            throw new ZeusException($"{path}.unitId 必须介于 0 与 31 之间。Host Link 单元号使用两位十进制站号。");
        }

        ParseHostLinkWordOrder(device.WordOrder, $"{path}.wordOrder");

        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidateHostLinkPoints(device.Points, path);
    }

    private static void ValidateMewtocolDevice(DeviceConfiguration device, string path)
    {
        if (device.UnitId is < 1 or > 99)
        {
            throw new ZeusException($"{path}.unitId 必须介于 1 与 99 之间。MEWTOCOL 站号使用两位十进制站号。");
        }

        ParseMewtocolWordOrder(device.WordOrder, $"{path}.wordOrder");

        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidateMewtocolPoints(device.Points, path);
    }

    private static void ValidateEtherNetIpDevice(DeviceConfiguration device, string path)
    {
        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidateEtherNetIpPoints(device.Points, path);
    }

    private static void ValidateDlt645Device(DeviceConfiguration device, string path)
    {
        ValidateDlt645Address(device.MeterAddress, $"{path}.meterAddress");
        ValidateDlt645BcdText(device.Password, 8, $"{path}.password");
        ValidateDlt645BcdText(device.OperatorCode, 8, $"{path}.operatorCode");

        if (device.WakeUpPreambleCount is < 0 or > 16)
        {
            throw new ZeusException($"{path}.wakeUpPreambleCount 必须介于 0 与 16 之间。");
        }

        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidateDlt645Points(device.Points, path);
    }

    private static void ValidateIec104Device(DeviceConfiguration device, string path)
    {
        ValidateUInt16(device.CommonAddress, $"{path}.commonAddress");
        ValidateByte(device.OriginatorAddress, $"{path}.originatorAddress");
        ValidateByte(device.InterrogationQualifier, $"{path}.interrogationQualifier");

        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        if (device.T1Milliseconds < 0)
        {
            throw new ZeusException($"{path}.t1Milliseconds 不能为负数。");
        }

        if (device.T2Milliseconds < 0)
        {
            throw new ZeusException($"{path}.t2Milliseconds 不能为负数。");
        }

        if (device.T3Milliseconds < 0)
        {
            throw new ZeusException($"{path}.t3Milliseconds 不能为负数。");
        }

        if (device.T1Milliseconds > 0 && device.T2Milliseconds > 0 && device.T2Milliseconds >= device.T1Milliseconds)
        {
            throw new ZeusException($"{path}.t2Milliseconds 必须小于 t1Milliseconds。");
        }

        if (device.MaxUnacknowledgedIFrames is < 1 or > 32767)
        {
            throw new ZeusException($"{path}.maxUnacknowledgedIFrames 必须介于 1 与 32767 之间。");
        }

        if (device.AcknowledgeWindow is < 1 || device.AcknowledgeWindow >= device.MaxUnacknowledgedIFrames)
        {
            throw new ZeusException($"{path}.acknowledgeWindow 必须介于 1 与 maxUnacknowledgedIFrames-1 之间。");
        }

        ValidateIec104Points(device.Points, path);
    }

    private static void ValidateMqttDevice(DeviceConfiguration device, string path)
    {
        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        if (device.MqttKeepAliveSeconds is < 0 or > ushort.MaxValue)
        {
            throw new ZeusException($"{path}.mqttKeepAliveSeconds 必须介于 0 与 65535 之间。");
        }

        if (device.MqttClientId is not null && string.IsNullOrWhiteSpace(device.MqttClientId))
        {
            throw new ZeusException($"{path}.mqttClientId 不能为空字符串。");
        }

        if (device.MqttUsername is null && device.MqttPassword is not null)
        {
            throw new ZeusException($"{path}.mqttPassword 不能在未设置 mqttUsername 时单独使用。");
        }

        if ((device.MqttWillTopic is null) != (device.MqttWillPayload is null))
        {
            throw new ZeusException($"{path}.mqttWillTopic 与 mqttWillPayload 必须同时设置或同时省略。");
        }

        var willQos = ParseMqttQualityOfService(device.MqttWillQos, $"{path}.mqttWillQos");
        if (device.MqttWillTopic is not null)
        {
            if (string.IsNullOrWhiteSpace(device.MqttWillTopic)
                || device.MqttWillTopic.Contains('+')
                || device.MqttWillTopic.Contains('#'))
            {
                throw new ZeusException($"{path}.mqttWillTopic 不能为空或包含 MQTT 通配符。");
            }
        }
        else if (device.MqttWillRetain || willQos != MqttQualityOfService.AtMostOnce)
        {
            throw new ZeusException($"{path} 未设置 MQTT 遗嘱时不能设置 mqttWillRetain 或非零 mqttWillQos。");
        }

        if (device.MqttMaximumPacketSize is < 2 or > 268_435_455)
        {
            throw new ZeusException($"{path}.mqttMaximumPacketSize 必须介于 2 与 268435455 之间。");
        }

        ValidateMqttPoints(device.Points, path);
    }

    private static void ValidateSnmpDevice(DeviceConfiguration device, string path)
    {
        if (device.TimeoutMilliseconds is <= 0)
        {
            throw new ZeusException($"{path}.timeoutMilliseconds 必须大于 0。");
        }

        ValidateCommunity(device.SnmpCommunity, $"{path}.snmpCommunity");
        if (device.SnmpWriteCommunity is not null)
        {
            ValidateCommunity(device.SnmpWriteCommunity, $"{path}.snmpWriteCommunity");
        }

        if (device.SnmpInitialRequestId <= 0)
        {
            throw new ZeusException($"{path}.snmpInitialRequestId 必须大于 0。");
        }

        ValidateSnmpPoints(device.Points, path);
    }

    private static void ValidateMcPoints(List<PointConfiguration> points, string devicePath, McFrameType frameType)
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

            if (string.IsNullOrWhiteSpace(point.DeviceCode))
            {
                throw new ZeusException($"{path}.deviceCode 必须指定。Mitsubishi MC 可选 D、M、X、Y、W、R、ZR。");
            }

            var deviceCode = ParseMcDeviceCode(point.DeviceCode, $"{path}.deviceCode");
            if (frameType == McFrameType.Frame1E && deviceCode == McDeviceCode.ExtendedFileRegister)
            {
                throw new ZeusException($"{path}.deviceCode 为 ZR，但 MC 1E 帧不支持 ZR。请改用 3e/4e，或移除该点。");
            }

            if (point.Address is < 0 or > 0xFFFFFF)
            {
                throw new ZeusException($"{path}.address 必须介于 0 与 16777215 之间。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            var isBit = IsMcBitDeviceCode(deviceCode);
            if (point.Scale is not null && isBit)
            {
                throw new ZeusException($"{path} 是 MC 位软元件，不能配置 scale。");
            }

            if ((point.LowAlarmLimit is not null || point.HighAlarmLimit is not null) && isBit)
            {
                throw new ZeusException($"{path} 是 MC 位软元件，不能配置 lowAlarmLimit 或 highAlarmLimit。");
            }

            if (point.Writable && deviceCode == McDeviceCode.InputRelay)
            {
                throw new ZeusException($"{path}.deviceCode 为 X 输入继电器，该软元件只读，不能设置 writable: true。");
            }
        }
    }

    private static void ValidateS7Points(List<PointConfiguration> points, string devicePath)
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

            if (string.IsNullOrWhiteSpace(point.Area))
            {
                throw new ZeusException($"{path}.area 必须指定。S7 可选 db、m、i、q。");
            }

            var area = ParseS7Area(point.Area, $"{path}.area");
            var dataType = ParseS7DataType(point.DataType, $"{path}.dataType");
            if (point.Address is < 0 or > 0x1FFFFF)
            {
                throw new ZeusException($"{path}.address 必须介于 0 与 2097151 之间。");
            }

            if (area == S7Area.DataBlock)
            {
                if (point.DbNumber is <= 0 or > ushort.MaxValue)
                {
                    throw new ZeusException($"{path}.db 必须介于 1 与 65535 之间。");
                }
            }
            else if (point.DbNumber != 0)
            {
                throw new ZeusException($"{path}.db 只能用于 S7 DB 区。");
            }

            if (dataType == S7DataType.Bool)
            {
                if (point.BitOffset is < 0 or > 7)
                {
                    throw new ZeusException($"{path}.bit 必须介于 0 与 7 之间。");
                }
            }
            else if (point.BitOffset != 0)
            {
                throw new ZeusException($"{path}.bit 只能用于 S7 bool 点。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            if (dataType == S7DataType.Bool)
            {
                if (point.Scale is not null)
                {
                    throw new ZeusException($"{path} 是 S7 bool 点，不能配置 scale。");
                }

                if (point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                {
                    throw new ZeusException($"{path} 是 S7 bool 点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
                }
            }

            if (point.Writable && area == S7Area.Inputs)
            {
                throw new ZeusException($"{path}.area 为 I 输入区，该区域只读，不能设置 writable: true。");
            }
        }
    }

    private static void ValidateFinsPoints(List<PointConfiguration> points, string devicePath)
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

            var dataType = ParseFinsDataType(point.DataType, $"{path}.dataType");
            if (string.IsNullOrWhiteSpace(point.Area))
            {
                throw new ZeusException($"{path}.area 必须指定。FINS 可选 cio、wr、hr、ar、dm、tc、em、em0–em18。");
            }

            var area = ParseFinsMemoryAreaCode(point.Area, dataType, $"{path}.area");
            if (point.Address is < 0 or > ushort.MaxValue)
            {
                throw new ZeusException($"{path}.address 必须介于 0 与 65535 之间。");
            }

            if (dataType == FinsDataType.Bit)
            {
                if (!area.IsBit)
                {
                    throw new ZeusException($"{path}.area「{point.Area}」不是 FINS 位区。");
                }

                if (point.BitOffset is < 0 or > 15)
                {
                    throw new ZeusException($"{path}.bit 必须介于 0 与 15 之间。");
                }
            }
            else
            {
                if (!area.IsWord)
                {
                    throw new ZeusException($"{path}.area「{point.Area}」不是 FINS 字区。");
                }

                if (point.BitOffset != 0)
                {
                    throw new ZeusException($"{path}.bit 只能用于 FINS bit 点。");
                }
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            if (dataType == FinsDataType.Bit)
            {
                if (point.Scale is not null)
                {
                    throw new ZeusException($"{path} 是 FINS bit 点，不能配置 scale。");
                }

                if (point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                {
                    throw new ZeusException($"{path} 是 FINS bit 点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
                }
            }
        }
    }

    private static void ValidateHostLinkPoints(List<PointConfiguration> points, string devicePath)
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

            var dataType = ParseHostLinkDataType(point.DataType, $"{path}.dataType");
            if (string.IsNullOrWhiteSpace(point.Area))
            {
                throw new ZeusException($"{path}.area 必须指定。Host Link 可选 cio、lr、hr、ar、dm。");
            }

            ParseHostLinkArea(point.Area, $"{path}.area");
            if (point.Address is < 0 or > 9999)
            {
                throw new ZeusException($"{path}.address 必须介于 0 与 9999 之间。");
            }

            if (dataType == HostLinkDataType.Bit)
            {
                if (point.BitOffset is < 0 or > 15)
                {
                    throw new ZeusException($"{path}.bit 必须介于 0 与 15 之间。");
                }
            }
            else if (point.BitOffset != 0)
            {
                throw new ZeusException($"{path}.bit 只能用于 Host Link bit 点。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            if (dataType == HostLinkDataType.Bit)
            {
                if (point.Scale is not null)
                {
                    throw new ZeusException($"{path} 是 Host Link bit 点，不能配置 scale。");
                }

                if (point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                {
                    throw new ZeusException($"{path} 是 Host Link bit 点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
                }
            }
        }
    }

    private static void ValidateMewtocolPoints(List<PointConfiguration> points, string devicePath)
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

            var dataType = ParseMewtocolDataType(point.DataType, $"{path}.dataType");
            if (string.IsNullOrWhiteSpace(point.Area))
            {
                throw new ZeusException($"{path}.area 必须指定。MEWTOCOL 数据区可选 dt、ld、fl；接点区可选 x、y、r、l。");
            }

            var areaText = point.Area;
            var isContact = TryParseMewtocolContactArea(areaText, out var contactArea);
            if (!isContact)
            {
                ParseMewtocolDataArea(areaText, $"{path}.area");
            }

            if (point.Address < 0 || point.Address > (isContact ? 9999 : 99999))
            {
                throw new ZeusException(isContact
                    ? $"{path}.address 必须介于 0 与 9999 之间。MEWTOCOL 接点区按 4 位字地址访问。"
                    : $"{path}.address 必须介于 0 与 99999 之间。MEWTOCOL 数据寄存器按 5 位字地址访问。");
            }

            if (dataType == MewtocolDataType.Bit)
            {
                if (point.BitOffset is < 0 or > 15)
                {
                    throw new ZeusException($"{path}.bit 必须介于 0 与 15 之间。");
                }
            }
            else if (point.BitOffset != 0)
            {
                throw new ZeusException($"{path}.bit 只能用于 MEWTOCOL bit 点。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            if (dataType == MewtocolDataType.Bit)
            {
                if (point.Scale is not null)
                {
                    throw new ZeusException($"{path} 是 MEWTOCOL bit 点，不能配置 scale。");
                }

                if (point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                {
                    throw new ZeusException($"{path} 是 MEWTOCOL bit 点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
                }
            }

            if (point.Writable && isContact && contactArea == MewtocolContactArea.ExternalInput)
            {
                throw new ZeusException($"{path}.area 为 X 输入区，该区域只读，不能设置 writable: true。");
            }
        }
    }

    private static void ValidateEtherNetIpPoints(List<PointConfiguration> points, string devicePath)
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

            if (point.Tag is not null && string.IsNullOrWhiteSpace(point.Tag))
            {
                throw new ZeusException($"{path}.tag 不能为空字符串。");
            }

            var dataType = ParseEtherNetIpDataType(point.DataType, $"{path}.dataType");
            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            if (dataType == EtherNetIpDataType.Bool)
            {
                if (point.Scale is not null)
                {
                    throw new ZeusException($"{path} 是 EtherNet/IP bool 点，不能配置 scale。");
                }

                if (point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                {
                    throw new ZeusException($"{path} 是 EtherNet/IP bool 点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
                }
            }
        }
    }

    private static void ValidateDlt645Points(List<PointConfiguration> points, string devicePath)
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

            var dataType = ParseDlt645DataType(point.DataType, $"{path}.dataType");
            if (point.Address < 0)
            {
                throw new ZeusException($"{path}.address 必须是非负 DL/T 645 数据项标识，例如 0x00000000。");
            }

            if (point.DataLength is < 1 or > 64)
            {
                throw new ZeusException($"{path}.dataLength 必须介于 1 与 64 之间。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            if (dataType == Dlt645DataType.RawBytes)
            {
                if (point.Scale is not null)
                {
                    throw new ZeusException($"{path} 是 DL/T 645 raw 点，不能配置 scale。");
                }

                if (point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                {
                    throw new ZeusException($"{path} 是 DL/T 645 raw 点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
                }
            }
        }
    }

    private static void ValidateIec104Points(List<PointConfiguration> points, string devicePath)
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

            var dataType = ParseIec104DataType(point.DataType, $"{path}.dataType");
            if (point.Address is < 0 or > 0xFFFFFF)
            {
                throw new ZeusException($"{path}.address 必须介于 0 与 16777215 之间，对应 3 字节 IOA。");
            }

            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            if (dataType == Iec104DataType.SinglePoint)
            {
                if (point.Scale is not null)
                {
                    throw new ZeusException($"{path} 是 IEC104 single-point 点，不能配置 scale。");
                }

                if (point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                {
                    throw new ZeusException($"{path} 是 IEC104 single-point 点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
                }
            }
        }
    }

    private static void ValidateMqttPoints(List<PointConfiguration> points, string devicePath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var topics = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var path = $"{devicePath}.points[{i}]";
            EnsureName(point.Name, path);
            if (!names.Add(point.Name.Trim()))
            {
                throw new ZeusException($"{path}.name「{point.Name}」在同一设备内重复。");
            }

            var topic = string.IsNullOrWhiteSpace(point.Topic) ? point.Name : point.Topic;
            if (string.IsNullOrWhiteSpace(topic) || topic.Contains('\0'))
            {
                throw new ZeusException($"{path}.topic 不能为空或包含空字符。");
            }

            topic = topic.Trim();
            if (topic.Contains('+') || topic.Contains('#'))
            {
                throw new ZeusException($"{path}.topic 不能包含 MQTT 通配符 + 或 #。");
            }

            if (!topics.Add(topic))
            {
                throw new ZeusException($"{path}.topic「{topic}」在同一设备内重复。");
            }

            var dataType = ParseMqttDataType(point.DataType, $"{path}.dataType");
            ParseMqttQualityOfService(point.MqttQos, $"{path}.mqttQos");
            if (point.Scale is not null)
            {
                throw new ZeusException($"{path}.scale 不是 MQTT 主题点的有效字段，请在上游发布工程值。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            if (dataType is MqttDataType.Text or MqttDataType.Boolean or MqttDataType.Bytes
                && (point.LowAlarmLimit is not null || point.HighAlarmLimit is not null))
            {
                throw new ZeusException($"{path} 的 {dataType} 点不能配置 lowAlarmLimit 或 highAlarmLimit。");
            }
        }
    }

    private static void ValidateSnmpPoints(List<PointConfiguration> points, string devicePath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var oids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var path = $"{devicePath}.points[{i}]";
            EnsureName(point.Name, path);
            if (!names.Add(point.Name.Trim()))
            {
                throw new ZeusException($"{path}.name「{point.Name}」在同一设备内重复。");
            }

            if (string.IsNullOrWhiteSpace(point.Oid))
            {
                throw new ZeusException($"{path}.oid 不能为空，例如 1.3.6.1.2.1.1.5.0。");
            }

            var oid = SnmpValue.ObjectIdentifier(point.Oid).Value?.ToString() ?? string.Empty;
            if (!oids.Add(oid))
            {
                throw new ZeusException($"{path}.oid「{oid}」在同一设备内重复。");
            }

            var dataType = ParseSnmpDataType(point.DataType, $"{path}.dataType");
            if (point.Scale is <= 0)
            {
                throw new ZeusException($"{path}.scale 必须大于 0。");
            }

            ValidateAlarmLimit(point.LowAlarmLimit, $"{path}.lowAlarmLimit");
            ValidateAlarmLimit(point.HighAlarmLimit, $"{path}.highAlarmLimit");
            if (point.LowAlarmLimit > point.HighAlarmLimit)
            {
                throw new ZeusException($"{path}.lowAlarmLimit 不能高于 highAlarmLimit。");
            }

            if (!IsSnmpNumeric(dataType))
            {
                if (point.Scale is not null)
                {
                    throw new ZeusException($"{path} 的 {dataType} 点不能配置 scale。");
                }

                if (point.LowAlarmLimit is not null || point.HighAlarmLimit is not null)
                {
                    throw new ZeusException($"{path} 的 {dataType} 点不能配置 lowAlarmLimit 或 highAlarmLimit。");
                }
            }
        }
    }

    private static void ValidateAlarmLimit(double? value, string path)
    {
        if (value is { } number && !double.IsFinite(number))
        {
            throw new ZeusException($"{path} 必须是有限数值。");
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

    internal static bool IsModbusDeviceType(string type)
        => type is "modbus-rtu" or "modbus-tcp" or "modbus-ascii";

    internal static bool IsModbusTcpDeviceType(string type)
        => type is "modbus-tcp";

    internal static bool IsModbusAsciiDeviceType(string type)
        => type is "modbus-ascii";

    internal static bool IsMcDeviceType(string type)
        => type is "mitsubishi-mc";

    internal static bool IsS7DeviceType(string type)
        => type is "siemens-s7";

    internal static bool IsFinsDeviceType(string type)
        => type is "omron-fins-udp" or "omron-fins-tcp";

    internal static bool IsFinsTcpDeviceType(string type)
        => type is "omron-fins-tcp";

    internal static bool IsHostLinkDeviceType(string type)
        => type is "omron-host-link";

    internal static bool IsMewtocolDeviceType(string type)
        => type is "panasonic-mewtocol";

    internal static bool IsEtherNetIpDeviceType(string type)
        => type is "ethernet-ip";

    internal static bool IsDlt645DeviceType(string type)
        => type is "dlt645";

    internal static bool IsIec104DeviceType(string type)
        => type is "iec104";

    internal static bool IsMqttDeviceType(string type)
        => type is "mqtt";

    internal static bool IsSnmpDeviceType(string type)
        => type is "snmp";

    internal static McFrameType ParseMcFrameType(string? value, string path)
    {
        var token = Normalize(value);
        return token switch
        {
            "1e" => McFrameType.Frame1E,
            "3e" or "" => McFrameType.Frame3E,
            "4e" => McFrameType.Frame4E,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 1e、3e、4e。")
        };
    }

    internal static McDataEncoding ParseMcDataEncoding(string? value, string path)
    {
        var token = Normalize(value);
        return token switch
        {
            "binary" or "" => McDataEncoding.Binary,
            "ascii" => McDataEncoding.Ascii,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 binary、ascii。")
        };
    }

    internal static McDeviceCode ParseMcDeviceCode(string? value, string path)
    {
        var token = Normalize(value);
        return token switch
        {
            "d" => McDeviceCode.DataRegister,
            "m" => McDeviceCode.InternalRelay,
            "x" => McDeviceCode.InputRelay,
            "y" => McDeviceCode.OutputRelay,
            "w" => McDeviceCode.LinkRegister,
            "r" => McDeviceCode.FileRegister,
            "zr" => McDeviceCode.ExtendedFileRegister,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 D、M、X、Y、W、R、ZR。")
        };
    }

    internal static bool IsMcWordDeviceCode(McDeviceCode deviceCode)
        => deviceCode is McDeviceCode.DataRegister
            or McDeviceCode.LinkRegister
            or McDeviceCode.FileRegister
            or McDeviceCode.ExtendedFileRegister;

    internal static bool IsMcBitDeviceCode(McDeviceCode deviceCode)
        => deviceCode is McDeviceCode.InternalRelay or McDeviceCode.InputRelay or McDeviceCode.OutputRelay;

    internal static S7Area ParseS7Area(string? value, string path)
    {
        return Normalize(value) switch
        {
            "db" => S7Area.DataBlock,
            "m" => S7Area.Merkers,
            "i" => S7Area.Inputs,
            "q" => S7Area.Outputs,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 db、m、i、q。")
        };
    }

    internal static S7DataType ParseS7DataType(string? value, string path)
    {
        return Normalize(value) switch
        {
            "bool" => S7DataType.Bool,
            "byte" => S7DataType.Byte,
            "word" => S7DataType.Word,
            "dword" => S7DataType.DWord,
            "int" => S7DataType.Int,
            "dint" => S7DataType.DInt,
            "real" => S7DataType.Real,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 bool、byte、word、dword、int、dint、real。")
        };
    }

    internal static FinsDataType ParseFinsDataType(string? value, string path)
    {
        return Normalize(value) switch
        {
            "" or "word" => FinsDataType.Word,
            "bit" => FinsDataType.Bit,
            "int16" => FinsDataType.Int16,
            "uint32" => FinsDataType.UInt32,
            "int32" => FinsDataType.Int32,
            "real" => FinsDataType.Real,
            _ => throw new ZeusException($"{path}「{value}」不受支持。FINS 可选 bit、word、int16、uint32、int32、real。")
        };
    }

    internal static HostLinkDataType ParseHostLinkDataType(string? value, string path)
    {
        return Normalize(value) switch
        {
            "" or "word" => HostLinkDataType.Word,
            "bit" => HostLinkDataType.Bit,
            "int16" => HostLinkDataType.Int16,
            "uint32" => HostLinkDataType.UInt32,
            "int32" => HostLinkDataType.Int32,
            "real" => HostLinkDataType.Real,
            _ => throw new ZeusException($"{path}「{value}」不受支持。Host Link 可选 bit、word、int16、uint32、int32、real。")
        };
    }

    internal static MewtocolDataType ParseMewtocolDataType(string? value, string path)
    {
        return Normalize(value) switch
        {
            "" or "word" => MewtocolDataType.Word,
            "bit" => MewtocolDataType.Bit,
            "int16" => MewtocolDataType.Int16,
            "uint32" => MewtocolDataType.UInt32,
            "int32" => MewtocolDataType.Int32,
            "real" => MewtocolDataType.Real,
            _ => throw new ZeusException($"{path}「{value}」不受支持。MEWTOCOL 可选 bit、word、int16、uint32、int32、real。")
        };
    }

    internal static HostLinkArea ParseHostLinkArea(string? value, string path)
    {
        return Normalize(value) switch
        {
            "cio" => HostLinkArea.Cio,
            "lr" => HostLinkArea.Link,
            "hr" => HostLinkArea.Holding,
            "ar" => HostLinkArea.Auxiliary,
            "dm" => HostLinkArea.DataMemory,
            _ => throw new ZeusException($"{path}「{value}」不受支持。Host Link 可选 cio、lr、hr、ar、dm。")
        };
    }

    internal static MewtocolDataArea ParseMewtocolDataArea(string? value, string path)
    {
        if (TryParseMewtocolDataArea(value, out var area))
        {
            return area;
        }

        throw new ZeusException($"{path}「{value}」不受支持。MEWTOCOL 数据区可选 dt、ld、fl；接点区可选 x、y、r、l。");
    }

    internal static bool TryParseMewtocolDataArea(string? value, out MewtocolDataArea area)
    {
        switch (Normalize(value))
        {
            case "dt":
                area = MewtocolDataArea.DataRegister;
                return true;
            case "ld":
                area = MewtocolDataArea.LinkDataRegister;
                return true;
            case "fl":
                area = MewtocolDataArea.FileRegister;
                return true;
            default:
                area = default;
                return false;
        }
    }

    internal static MewtocolContactArea ParseMewtocolContactArea(string? value, string path)
    {
        if (TryParseMewtocolContactArea(value, out var area))
        {
            return area;
        }

        throw new ZeusException($"{path}「{value}」不受支持。MEWTOCOL 接点区可选 x、y、r、l。");
    }

    internal static bool TryParseMewtocolContactArea(string? value, out MewtocolContactArea area)
    {
        switch (Normalize(value))
        {
            case "x":
                area = MewtocolContactArea.ExternalInput;
                return true;
            case "y":
                area = MewtocolContactArea.ExternalOutput;
                return true;
            case "r":
                area = MewtocolContactArea.InternalRelay;
                return true;
            case "l":
                area = MewtocolContactArea.LinkRelay;
                return true;
            default:
                area = default;
                return false;
        }
    }

    internal static FinsWordOrder ParseFinsWordOrder(string? value, string path)
    {
        return Normalize(value) switch
        {
            "" or "high-word-first" => FinsWordOrder.HighWordFirst,
            "low-word-first" => FinsWordOrder.LowWordFirst,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 high-word-first、low-word-first。")
        };
    }

    internal static HostLinkWordOrder ParseHostLinkWordOrder(string? value, string path)
    {
        return Normalize(value) switch
        {
            "" or "high-word-first" => HostLinkWordOrder.HighWordFirst,
            "low-word-first" => HostLinkWordOrder.LowWordFirst,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 high-word-first、low-word-first。")
        };
    }

    internal static MewtocolWordOrder ParseMewtocolWordOrder(string? value, string path)
    {
        return Normalize(value) switch
        {
            "" or "high-word-first" => MewtocolWordOrder.HighWordFirst,
            "low-word-first" => MewtocolWordOrder.LowWordFirst,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 high-word-first、low-word-first。")
        };
    }

    internal static FinsMemoryAreaCode ParseFinsMemoryAreaCode(string? value, FinsDataType dataType, string path)
    {
        var token = Normalize(value);
        var bit = dataType == FinsDataType.Bit;
        if (token.StartsWith("em", StringComparison.Ordinal) && token.Length > 2 && int.TryParse(token[2..], out var bank))
        {
            return bit ? FinsMemoryAreaCode.EmBankBit(bank) : FinsMemoryAreaCode.EmBankWord(bank);
        }

        return token switch
        {
            "cio" => bit ? FinsMemoryAreaCode.CioBit : FinsMemoryAreaCode.CioWord,
            "wr" => bit ? FinsMemoryAreaCode.WorkBit : FinsMemoryAreaCode.WorkWord,
            "hr" => bit ? FinsMemoryAreaCode.HoldingBit : FinsMemoryAreaCode.HoldingWord,
            "ar" => bit ? FinsMemoryAreaCode.AuxiliaryBit : FinsMemoryAreaCode.AuxiliaryWord,
            "dm" => bit ? FinsMemoryAreaCode.DataMemoryBit : FinsMemoryAreaCode.DataMemoryWord,
            "tc" => bit ? FinsMemoryAreaCode.TimerCounterFlag : FinsMemoryAreaCode.TimerCounterValue,
            "em" => bit ? FinsMemoryAreaCode.CurrentEmBit : FinsMemoryAreaCode.CurrentEmWord,
            _ => throw new ZeusException($"{path}「{value}」不受支持。FINS 可选 cio、wr、hr、ar、dm、tc、em、em0–em18。")
        };
    }

    internal static EtherNetIpDataType ParseEtherNetIpDataType(string? value, string path)
    {
        return Normalize(value) switch
        {
            "bool" => EtherNetIpDataType.Bool,
            "sint" => EtherNetIpDataType.SInt,
            "int" => EtherNetIpDataType.Int,
            "dint" => EtherNetIpDataType.DInt,
            "lint" => EtherNetIpDataType.LInt,
            "usint" => EtherNetIpDataType.USInt,
            "uint" => EtherNetIpDataType.UInt,
            "udint" => EtherNetIpDataType.UDInt,
            "ulint" => EtherNetIpDataType.ULInt,
            "real" => EtherNetIpDataType.Real,
            "lreal" => EtherNetIpDataType.LReal,
            _ => throw new ZeusException($"{path}「{value}」不受支持。EtherNet/IP 可选 bool、sint、int、dint、lint、usint、uint、udint、ulint、real、lreal。")
        };
    }

    internal static Dlt645DataType ParseDlt645DataType(string? value, string path)
    {
        return Normalize(value) switch
        {
            "" or "bcd" => Dlt645DataType.Bcd,
            "raw" => Dlt645DataType.RawBytes,
            _ => throw new ZeusException($"{path}「{value}」不受支持。DL/T 645 可选 bcd、raw。")
        };
    }

    internal static Iec104DataType ParseIec104DataType(string? value, string path)
    {
        return Normalize(value) switch
        {
            "single-point" => Iec104DataType.SinglePoint,
            "normalized" => Iec104DataType.Normalized,
            "scaled" => Iec104DataType.Scaled,
            "short-float" => Iec104DataType.ShortFloat,
            _ => throw new ZeusException($"{path}「{value}」不受支持。IEC104 可选 single-point、normalized、scaled、short-float。")
        };
    }

    internal static MqttDataType ParseMqttDataType(string? value, string path)
    {
        return Normalize(value) switch
        {
            "" or "text" => MqttDataType.Text,
            "boolean" => MqttDataType.Boolean,
            "int32" => MqttDataType.Int32,
            "int64" => MqttDataType.Int64,
            "double" => MqttDataType.Double,
            "bytes" => MqttDataType.Bytes,
            _ => throw new ZeusException($"{path}「{value}」不受支持。MQTT 可选 text、boolean、int32、int64、double、bytes。")
        };
    }

    internal static SnmpDataType ParseSnmpDataType(string? value, string path)
    {
        return Normalize(value) switch
        {
            "integer" => SnmpDataType.Integer,
            "gauge32" => SnmpDataType.Gauge32,
            "counter32" => SnmpDataType.Counter32,
            "timeticks" => SnmpDataType.TimeTicks,
            "text" => SnmpDataType.Text,
            "octet-string" => SnmpDataType.OctetString,
            "oid" => SnmpDataType.ObjectIdentifier,
            "ip-address" => SnmpDataType.IpAddress,
            _ => throw new ZeusException($"{path}「{value}」不受支持。SNMP 可选 integer、gauge32、counter32、timeticks、text、octet-string、oid、ip-address。")
        };
    }

    internal static bool IsSnmpNumeric(SnmpDataType dataType)
        => dataType is SnmpDataType.Integer or SnmpDataType.Counter32 or SnmpDataType.Gauge32 or SnmpDataType.TimeTicks;

    internal static MqttQualityOfService ParseMqttQualityOfService(string? value, string path)
    {
        return Normalize(value) switch
        {
            "" or "0" => MqttQualityOfService.AtMostOnce,
            "1" => MqttQualityOfService.AtLeastOnce,
            "2" => MqttQualityOfService.ExactlyOnce,
            _ => throw new ZeusException($"{path}「{value}」不受支持。MQTT QoS 可选 0、1、2。")
        };
    }

    private static void ValidateDlt645Address(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ZeusException($"{path} 不能为空，应为 12 位十进制字符串，例如 000000000001。");
        }

        var normalized = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != 12 || normalized.Any(ch => ch is < '0' or > '9'))
        {
            throw new ZeusException($"{path}「{value}」无效，应为 12 位十进制字符串，例如 000000000001。");
        }
    }

    private static void ValidateDlt645BcdText(string? value, int length, string path)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != length || value.Trim().Any(ch => ch is < '0' or > '9'))
        {
            throw new ZeusException($"{path} 必须是 {length} 位十进制字符串。");
        }
    }

    private static void ValidateCommunity(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ZeusException($"{path} 不能为空。");
        }
    }

    private static void ValidateByte(int value, string path)
    {
        if (value is < 0 or > byte.MaxValue)
        {
            throw new ZeusException($"{path} 必须介于 0 与 255 之间。");
        }
    }

    private static void ValidateUInt16(int value, string path)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new ZeusException($"{path} 必须介于 0 与 65535 之间。");
        }
    }
}
