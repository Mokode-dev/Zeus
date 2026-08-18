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
                case "tcp-server" or "tcpserver":
                    ValidateTcpServerChannel(channel, path);
                    break;
                case "udp-server" or "udpserver":
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
            else if (IsMcDeviceType(type))
            {
                ValidateMcDevice(device, path);
            }
            else
            {
                throw new ZeusException($"{path}.type「{device.Type}」不受支持。可选 modbus-rtu、modbus-tcp、modbus-ascii、mitsubishi-mc、siemens-s7、omron-fins、omron-host-link、panasonic-mewtocol、ethernet-ip。");
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
        if (responder is not ("modbus" or "mc" or "mitsubishi-mc" or "mitsubishimc" or "s7" or "siemens-s7" or "siemenss7" or "fins" or "omron-fins" or "omronfins" or "host-link" or "hostlink" or "omron-host-link" or "omronhostlink" or "mewtocol" or "panasonic-mewtocol" or "panasonicmewtocol" or "ethernet-ip" or "ethernetip" or "cip" or "allen-bradley" or "allenbradley"))
        {
            throw new ZeusException($"{path}.responder「{channel.Responder}」不受支持。当前支持 modbus、mc、s7、fins、host-link、mewtocol、ethernet-ip，或省略以回显写入。");
        }

        if (responder is "host-link" or "hostlink" or "omron-host-link" or "omronhostlink")
        {
            if (channel.UnitId > 31)
            {
                throw new ZeusException($"{path}.unitId 必须介于 0 与 31 之间。Host Link 虚拟 PLC 使用两位十进制单元号。");
            }

            return;
        }

        if (responder is "mewtocol" or "panasonic-mewtocol" or "panasonicmewtocol")
        {
            if (channel.UnitId is < 1 or > 99)
            {
                throw new ZeusException($"{path}.unitId 必须介于 1 与 99 之间。MEWTOCOL 虚拟 PLC 使用两位十进制站号。");
            }

            return;
        }

        if (responder is "mc" or "mitsubishi-mc" or "mitsubishimc" or "s7" or "siemens-s7" or "siemenss7" or "ethernet-ip" or "ethernetip" or "cip" or "allen-bradley" or "allenbradley")
        {
            return;
        }

        var transport = Normalize(channel.Transport);
        if (responder is "fins" or "omron-fins" or "omronfins")
        {
            if (transport is not ("udp" or "tcp" or "rtu"))
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
            if (table is not ("holding" or "holdingregister" or "input" or "inputregister" or "coil" or "discrete" or "discreteinput"))
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
                && table is "coil" or "discrete" or "discreteinput")
            {
                throw new ZeusException($"{path} 是布尔点，不能配置 lowAlarmLimit 或 highAlarmLimit。");
            }

            if (point.Writable && table is "input" or "inputregister" or "discrete" or "discreteinput")
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

            var deviceCode = ParseMcDeviceCode(point.DeviceCode ?? point.Table, $"{path}.deviceCode");
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
            var area = ParseFinsMemoryAreaCode(point.Area ?? point.Table, dataType, $"{path}.area");
            if (point.Address is < 0 or > ushort.MaxValue)
            {
                throw new ZeusException($"{path}.address 必须介于 0 与 65535 之间。");
            }

            if (dataType == FinsDataType.Bit)
            {
                if (!area.IsBit)
                {
                    throw new ZeusException($"{path}.area「{point.Area ?? point.Table}」不是 FINS 位区。");
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
                    throw new ZeusException($"{path}.area「{point.Area ?? point.Table}」不是 FINS 字区。");
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
            ParseHostLinkArea(point.Area ?? point.Table, $"{path}.area");
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
            var areaText = point.Area ?? point.Table;
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

            if (point.TagName is not null && string.IsNullOrWhiteSpace(point.TagName))
            {
                throw new ZeusException($"{path}.tagName 不能为空字符串。");
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
        => type is "modbus-rtu" or "modbusrtu" or "rtu" or "modbus-tcp" or "modbustcp" or "tcp" or "modbus-ascii" or "modbusascii" or "ascii";

    internal static bool IsModbusTcpDeviceType(string type)
        => type is "modbus-tcp" or "modbustcp" or "tcp";

    internal static bool IsModbusAsciiDeviceType(string type)
        => type is "modbus-ascii" or "modbusascii" or "ascii";

    internal static bool IsMcDeviceType(string type)
        => type is "mitsubishi-mc" or "mitsubishimc" or "mc" or "melsec-mc" or "melsecmc" or "mc-3e" or "mc3e";

    internal static bool IsS7DeviceType(string type)
        => type is "siemens-s7" or "siemenss7" or "s7" or "s7-comm" or "s7comm";

    internal static bool IsFinsDeviceType(string type)
        => type is "omron-fins" or "omronfins" or "fins" or "fins-udp" or "finsudp" or "omron-fins-udp" or "omronfinsudp" or "fins-tcp" or "finstcp" or "omron-fins-tcp" or "omronfinstcp";

    internal static bool IsFinsTcpDeviceType(string type)
        => type is "fins-tcp" or "finstcp" or "omron-fins-tcp" or "omronfinstcp";

    internal static bool IsHostLinkDeviceType(string type)
        => type is "omron-host-link" or "omronhostlink" or "host-link" or "hostlink" or "omron-hostlink";

    internal static bool IsMewtocolDeviceType(string type)
        => type is "panasonic-mewtocol" or "panasonicmewtocol" or "mewtocol" or "mewtocol-com" or "mewtocolcom" or "panasonic";

    internal static bool IsEtherNetIpDeviceType(string type)
        => type is "ethernet-ip" or "ethernetip" or "ether-net-ip" or "cip" or "ab-cip" or "abcip" or "allen-bradley" or "allenbradley" or "ab-ethernet-ip" or "abethernetip";

    internal static McFrameType ParseMcFrameType(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "1e" or "frame1e" => McFrameType.Frame1E,
            "3e" or "frame3e" or "" => McFrameType.Frame3E,
            "4e" or "frame4e" => McFrameType.Frame4E,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 1e、3e、4e。")
        };
    }

    internal static McDataEncoding ParseMcDataEncoding(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "binary" or "bin" or "" => McDataEncoding.Binary,
            "ascii" or "asc" => McDataEncoding.Ascii,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 binary、ascii。")
        };
    }

    internal static McDeviceCode ParseMcDeviceCode(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "d" or "data" or "dataregister" or "holding" or "holdingregister" => McDeviceCode.DataRegister,
            "m" or "internal" or "internalrelay" or "coil" => McDeviceCode.InternalRelay,
            "x" or "input" or "inputrelay" => McDeviceCode.InputRelay,
            "y" or "output" or "outputrelay" => McDeviceCode.OutputRelay,
            "w" or "link" or "linkregister" => McDeviceCode.LinkRegister,
            "r" or "file" or "fileregister" => McDeviceCode.FileRegister,
            "zr" or "extendedfile" or "extendedfileregister" => McDeviceCode.ExtendedFileRegister,
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
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "db" or "datablock" or "data" => S7Area.DataBlock,
            "m" or "marker" or "markers" or "merker" or "merkers" => S7Area.Merkers,
            "i" or "input" or "inputs" => S7Area.Inputs,
            "q" or "output" or "outputs" => S7Area.Outputs,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 db、m、i、q。")
        };
    }

    internal static S7DataType ParseS7DataType(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "bool" or "bit" => S7DataType.Bool,
            "byte" or "b" => S7DataType.Byte,
            "word" or "w" or "uint16" or "ushort" => S7DataType.Word,
            "dword" or "dw" or "uint32" or "uint" => S7DataType.DWord,
            "int" or "int16" or "short" => S7DataType.Int,
            "dint" or "int32" => S7DataType.DInt,
            "real" or "float" or "single" => S7DataType.Real,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 bool、byte、word、dword、int、dint、real。")
        };
    }

    internal static FinsDataType ParseFinsDataType(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "" or "word" or "w" or "uint16" or "ushort" => FinsDataType.Word,
            "bit" or "bool" or "boolean" => FinsDataType.Bit,
            "int" or "int16" or "short" => FinsDataType.Int16,
            "uint32" or "udint" or "dword" or "dw" => FinsDataType.UInt32,
            "int32" or "dint" => FinsDataType.Int32,
            "real" or "float" or "single" => FinsDataType.Real,
            _ => throw new ZeusException($"{path}「{value}」不受支持。FINS 可选 bit、word、int16、uint32、int32、real。")
        };
    }

    internal static HostLinkDataType ParseHostLinkDataType(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "" or "word" or "w" or "uint16" or "ushort" => HostLinkDataType.Word,
            "bit" or "bool" or "boolean" => HostLinkDataType.Bit,
            "int" or "int16" or "short" => HostLinkDataType.Int16,
            "uint32" or "udint" or "dword" or "dw" => HostLinkDataType.UInt32,
            "int32" or "dint" => HostLinkDataType.Int32,
            "real" or "float" or "single" => HostLinkDataType.Real,
            _ => throw new ZeusException($"{path}「{value}」不受支持。Host Link 可选 bit、word、int16、uint32、int32、real。")
        };
    }

    internal static MewtocolDataType ParseMewtocolDataType(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "" or "word" or "w" or "uint16" or "ushort" => MewtocolDataType.Word,
            "bit" or "bool" or "boolean" => MewtocolDataType.Bit,
            "int" or "int16" or "short" => MewtocolDataType.Int16,
            "uint32" or "udint" or "dword" or "dw" => MewtocolDataType.UInt32,
            "int32" or "dint" => MewtocolDataType.Int32,
            "real" or "float" or "single" => MewtocolDataType.Real,
            _ => throw new ZeusException($"{path}「{value}」不受支持。MEWTOCOL 可选 bit、word、int16、uint32、int32、real。")
        };
    }

    internal static HostLinkArea ParseHostLinkArea(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "" or "cio" or "ir" or "inputoutput" => HostLinkArea.Cio,
            "lr" or "link" => HostLinkArea.Link,
            "hr" or "holding" => HostLinkArea.Holding,
            "ar" or "aux" or "auxiliary" => HostLinkArea.Auxiliary,
            "dm" or "data" or "datamemory" => HostLinkArea.DataMemory,
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
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        switch (token)
        {
            case "" or "dt" or "d" or "data" or "dataregister":
                area = MewtocolDataArea.DataRegister;
                return true;
            case "ld" or "linkdata" or "linkdataregister":
                area = MewtocolDataArea.LinkDataRegister;
                return true;
            case "fl" or "f" or "file" or "fileregister":
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
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        switch (token)
        {
            case "x" or "input" or "externalinput":
                area = MewtocolContactArea.ExternalInput;
                return true;
            case "y" or "output" or "externaloutput":
                area = MewtocolContactArea.ExternalOutput;
                return true;
            case "r" or "relay" or "internal" or "internalrelay":
                area = MewtocolContactArea.InternalRelay;
                return true;
            case "l" or "lr" or "link" or "linkrelay":
                area = MewtocolContactArea.LinkRelay;
                return true;
            default:
                area = default;
                return false;
        }
    }

    internal static FinsWordOrder ParseFinsWordOrder(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "" or "highwordfirst" or "highfirst" or "big" or "bigendian" => FinsWordOrder.HighWordFirst,
            "lowwordfirst" or "lowfirst" or "little" or "littleendian" => FinsWordOrder.LowWordFirst,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 high-word-first、low-word-first。")
        };
    }

    internal static HostLinkWordOrder ParseHostLinkWordOrder(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "" or "highwordfirst" or "highfirst" or "big" or "bigendian" => HostLinkWordOrder.HighWordFirst,
            "lowwordfirst" or "lowfirst" or "little" or "littleendian" => HostLinkWordOrder.LowWordFirst,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 high-word-first、low-word-first。")
        };
    }

    internal static MewtocolWordOrder ParseMewtocolWordOrder(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "" or "highwordfirst" or "highfirst" or "big" or "bigendian" => MewtocolWordOrder.HighWordFirst,
            "lowwordfirst" or "lowfirst" or "little" or "littleendian" => MewtocolWordOrder.LowWordFirst,
            _ => throw new ZeusException($"{path}「{value}」不受支持。可选 high-word-first、low-word-first。")
        };
    }

    internal static FinsMemoryAreaCode ParseFinsMemoryAreaCode(string? value, FinsDataType dataType, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        var bit = dataType == FinsDataType.Bit || token.EndsWith("bit", StringComparison.Ordinal);
        var word = dataType != FinsDataType.Bit || token.EndsWith("word", StringComparison.Ordinal);
        var compact = token.Replace("word", string.Empty, StringComparison.Ordinal).Replace("bit", string.Empty, StringComparison.Ordinal);
        if (compact.StartsWith("em", StringComparison.Ordinal) && compact.Length > 2 && int.TryParse(compact[2..], out var bank))
        {
            return bit ? FinsMemoryAreaCode.EmBankBit(bank) : FinsMemoryAreaCode.EmBankWord(bank);
        }

        return compact switch
        {
            "cio" or "" => bit ? FinsMemoryAreaCode.CioBit : FinsMemoryAreaCode.CioWord,
            "wr" or "work" => bit ? FinsMemoryAreaCode.WorkBit : FinsMemoryAreaCode.WorkWord,
            "hr" or "holding" => bit ? FinsMemoryAreaCode.HoldingBit : FinsMemoryAreaCode.HoldingWord,
            "ar" or "aux" or "auxiliary" => bit ? FinsMemoryAreaCode.AuxiliaryBit : FinsMemoryAreaCode.AuxiliaryWord,
            "dm" or "data" or "datamemory" => bit ? FinsMemoryAreaCode.DataMemoryBit : FinsMemoryAreaCode.DataMemoryWord,
            "tc" or "timcnt" or "timercounter" or "timer" or "counter" => bit ? FinsMemoryAreaCode.TimerCounterFlag : FinsMemoryAreaCode.TimerCounterValue,
            "em" or "currentem" or "emcurrent" => bit ? FinsMemoryAreaCode.CurrentEmBit : FinsMemoryAreaCode.CurrentEmWord,
            _ => throw new ZeusException($"{path}「{value}」不受支持。FINS 可选 cio、wr、hr、ar、dm、tc、em、em0–em18。")
        };
    }

    internal static EtherNetIpDataType ParseEtherNetIpDataType(string? value, string path)
    {
        var token = Normalize(value).Replace("-", string.Empty, StringComparison.Ordinal);
        return token switch
        {
            "" or "dint" or "int32" => EtherNetIpDataType.DInt,
            "bool" or "boolean" or "bit" => EtherNetIpDataType.Bool,
            "sint" or "int8" or "sbyte" => EtherNetIpDataType.SInt,
            "int" or "int16" or "short" => EtherNetIpDataType.Int,
            "lint" or "int64" or "long" => EtherNetIpDataType.LInt,
            "usint" or "uint8" or "byte" => EtherNetIpDataType.USInt,
            "uint" or "uint16" or "ushort" or "word" => EtherNetIpDataType.UInt,
            "udint" or "uint32" or "dword" => EtherNetIpDataType.UDInt,
            "ulint" or "uint64" or "ulong" => EtherNetIpDataType.ULInt,
            "real" or "float" or "single" => EtherNetIpDataType.Real,
            "lreal" or "double" => EtherNetIpDataType.LReal,
            _ => throw new ZeusException($"{path}「{value}」不受支持。EtherNet/IP 可选 bool、sint、int、dint、lint、usint、uint、udint、ulint、real、lreal。")
        };
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
