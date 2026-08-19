using System.Buffers.Binary;
using System.Text;

namespace Zeus;

internal enum MqttPacketType : byte
{
    Connect = 1,
    ConnAck = 2,
    Publish = 3,
    PubAck = 4,
    PubRec = 5,
    PubRel = 6,
    PubComp = 7,
    Subscribe = 8,
    SubAck = 9,
    Unsubscribe = 10,
    UnsubAck = 11,
    PingReq = 12,
    PingResp = 13,
    Disconnect = 14
}

internal readonly record struct MqttPacket(MqttPacketType Type, byte Flags, byte[] Body);

internal readonly record struct MqttPublishPacket(
    string Topic,
    byte[] Payload,
    bool Retain,
    bool Duplicate,
    MqttQualityOfService QualityOfService,
    ushort? PacketIdentifier);

internal readonly record struct MqttSubscription(string TopicFilter, MqttQualityOfService QualityOfService);

internal readonly record struct MqttConnectPacket(
    string ClientId,
    bool CleanSession,
    ushort KeepAliveSeconds,
    string? Username,
    string? WillTopic,
    byte[]? WillPayload,
    MqttQualityOfService WillQualityOfService,
    bool WillRetain);

internal static class MqttCodec
{
    private const int MaximumRemainingLength = 268_435_455;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] EncodeConnect(MqttOptions options, string fallbackClientId)
    {
        ValidateOptions(options);
        var clientId = string.IsNullOrWhiteSpace(options.ClientId) ? fallbackClientId : options.ClientId.Trim();
        EnsureUtf8(clientId, "clientId");

        var body = new List<byte>();
        WriteUtf8(body, "MQTT");
        body.Add(4);
        var flags = (byte)(options.CleanSession ? 0x02 : 0);
        if (options.WillTopic is not null)
        {
            flags |= 0x04;
            flags |= (byte)((int)options.WillQualityOfService << 3);
            if (options.WillRetain)
            {
                flags |= 0x20;
            }
        }

        if (options.Username is not null)
        {
            flags |= 0x80;
            if (options.Password is not null)
            {
                flags |= 0x40;
            }
        }

        body.Add(flags);
        WriteUInt16(body, options.KeepAliveSeconds);
        WriteUtf8(body, clientId);
        if (options.WillTopic is not null)
        {
            WriteUtf8(body, options.WillTopic);
            WriteBinary(body, options.WillPayload!);
        }

        if (options.Username is not null)
        {
            WriteUtf8(body, options.Username);
            if (options.Password is not null)
            {
                WriteUtf8(body, options.Password);
            }
        }

        return EncodeFixed(MqttPacketType.Connect, 0, body.ToArray());
    }

    public static byte[] EncodeSubscribe(ushort packetId, string topicFilter, MqttQualityOfService qualityOfService)
    {
        EnsurePacketIdentifier(packetId);
        EnsureTopicFilter(topicFilter);
        ValidateQualityOfService(qualityOfService);
        var body = new List<byte>();
        WriteUInt16(body, packetId);
        WriteUtf8(body, topicFilter.Trim());
        body.Add((byte)qualityOfService);
        return EncodeFixed(MqttPacketType.Subscribe, 2, body.ToArray());
    }

    public static byte[] EncodeUnsubscribe(ushort packetId, string topicFilter)
    {
        EnsurePacketIdentifier(packetId);
        EnsureTopicFilter(topicFilter);
        var body = new List<byte>();
        WriteUInt16(body, packetId);
        WriteUtf8(body, topicFilter.Trim());
        return EncodeFixed(MqttPacketType.Unsubscribe, 2, body.ToArray());
    }

    public static byte[] EncodePublish(
        string topic,
        ReadOnlySpan<byte> payload,
        bool retain,
        MqttQualityOfService qualityOfService = MqttQualityOfService.AtMostOnce,
        ushort? packetIdentifier = null,
        bool duplicate = false)
    {
        EnsureTopicName(topic);
        ValidateQualityOfService(qualityOfService);
        if (qualityOfService == MqttQualityOfService.AtMostOnce && packetIdentifier is not null)
        {
            throw new ZeusException("MQTT QoS 0 PUBLISH 不能包含报文标识符。");
        }

        if (qualityOfService != MqttQualityOfService.AtMostOnce)
        {
            EnsurePacketIdentifier(packetIdentifier ?? 0);
        }

        var body = new List<byte>(2 + StrictUtf8.GetByteCount(topic) + 2 + payload.Length);
        WriteUtf8(body, topic.Trim());
        if (packetIdentifier is { } identifier)
        {
            WriteUInt16(body, identifier);
        }

        body.AddRange(payload.ToArray());
        var flags = (byte)((retain ? 1 : 0) | ((int)qualityOfService << 1) | (duplicate ? 8 : 0));
        return EncodeFixed(MqttPacketType.Publish, flags, body.ToArray());
    }

    public static byte[] EncodeConnAck(byte returnCode = 0, bool sessionPresent = false)
    {
        if (returnCode != 0 && sessionPresent)
        {
            throw new ZeusException("MQTT CONNACK 拒绝连接时不能设置 Session Present。");
        }

        return EncodeFixed(MqttPacketType.ConnAck, 0, [(byte)(sessionPresent ? 1 : 0), returnCode]);
    }

    public static byte[] EncodeSubAck(ushort packetId, MqttQualityOfService qualityOfService)
    {
        EnsurePacketIdentifier(packetId);
        ValidateQualityOfService(qualityOfService);
        var body = new byte[3];
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(0, 2), packetId);
        body[2] = (byte)qualityOfService;
        return EncodeFixed(MqttPacketType.SubAck, 0, body);
    }

    public static byte[] EncodeAcknowledgement(MqttPacketType type, ushort packetId)
    {
        EnsurePacketIdentifier(packetId);
        if (type is not (MqttPacketType.PubAck or MqttPacketType.PubRec or MqttPacketType.PubRel or MqttPacketType.PubComp or MqttPacketType.UnsubAck))
        {
            throw new ZeusException($"MQTT {type} 不是报文标识符确认类型。");
        }

        Span<byte> body = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(body, packetId);
        return EncodeFixed(type, (byte)(type == MqttPacketType.PubRel ? 2 : 0), body);
    }

    public static byte[] EncodePingResp() => [0xD0, 0x00];

    public static byte[] EncodePingReq() => [0xC0, 0x00];

    public static byte[] EncodeDisconnect() => [0xE0, 0x00];

    public static byte[] EncodeFixed(MqttPacketType type, byte flags, ReadOnlySpan<byte> body)
    {
        if (body.Length > MaximumRemainingLength)
        {
            throw new ZeusException("MQTT 报文超过协议允许的最大剩余长度 268435455。");
        }

        var output = new List<byte>(body.Length + 5) { (byte)(((byte)type << 4) | (flags & 0x0F)) };
        WriteRemainingLength(output, body.Length);
        output.AddRange(body.ToArray());
        return output.ToArray();
    }

    public static bool TryDecodePacket(List<byte> buffer, out MqttPacket packet, out int consumed, int maximumPacketSize = 1024 * 1024)
    {
        packet = default;
        consumed = 0;
        if (buffer.Count < 2)
        {
            return false;
        }

        var first = buffer[0];
        var type = (MqttPacketType)(first >> 4);
        var flags = (byte)(first & 0x0F);
        ValidateFixedHeader(type, flags);

        var multiplier = 1;
        var remaining = 0;
        var index = 1;
        var terminated = false;
        for (var i = 0; i < 4; i++)
        {
            if (index >= buffer.Count)
            {
                return false;
            }

            var encoded = buffer[index++];
            remaining += (encoded & 0x7F) * multiplier;
            if (encoded < 0x80)
            {
                terminated = true;
                break;
            }

            multiplier *= 128;
        }

        if (!terminated)
        {
            throw new MqttException("MQTT 剩余长度编码超过 4 个字节。");
        }

        if (remaining > maximumPacketSize || index > maximumPacketSize - remaining)
        {
            throw new MqttException($"MQTT 报文长度 {index + remaining} 超过允许值 {maximumPacketSize}。");
        }

        if (buffer.Count < index + remaining)
        {
            return false;
        }

        packet = new MqttPacket(type, flags, buffer.GetRange(index, remaining).ToArray());
        consumed = index + remaining;
        return true;
    }

    public static MqttConnectPacket DecodeConnect(MqttPacket packet)
    {
        if (packet.Type != MqttPacketType.Connect)
        {
            throw new MqttException("报文不是 MQTT CONNECT。");
        }

        var (protocolName, offset) = ReadUtf8(packet.Body, 0, "协议名");
        if (protocolName != "MQTT" || offset + 4 > packet.Body.Length || packet.Body[offset] != 4)
        {
            throw new MqttException("MQTT CONNECT 协议名或版本不是 MQTT 3.1.1。");
        }

        var flags = packet.Body[offset + 1];
        if ((flags & 1) != 0 || (flags & 0x40) != 0 && (flags & 0x80) == 0)
        {
            throw new MqttException("MQTT CONNECT 标志无效。");
        }

        var willEnabled = (flags & 0x04) != 0;
        var willQos = (MqttQualityOfService)((flags >> 3) & 0x03);
        var willRetain = (flags & 0x20) != 0;
        if ((!willEnabled && (willQos != 0 || willRetain)) || willQos > MqttQualityOfService.ExactlyOnce)
        {
            throw new MqttException("MQTT CONNECT 遗嘱标志无效。");
        }

        var keepAlive = BinaryPrimitives.ReadUInt16BigEndian(packet.Body.AsSpan(offset + 2, 2));
        offset += 4;
        var (clientId, payloadOffset) = ReadUtf8(packet.Body, offset, "客户端标识");
        offset = payloadOffset;
        string? willTopic = null;
        byte[]? willPayload = null;
        if (willEnabled)
        {
            (willTopic, offset) = ReadUtf8(packet.Body, offset, "遗嘱主题");
            (willPayload, offset) = ReadBinary(packet.Body, offset, "遗嘱载荷");
        }

        string? username = null;
        if ((flags & 0x80) != 0)
        {
            (username, offset) = ReadUtf8(packet.Body, offset, "用户名");
        }

        if ((flags & 0x40) != 0)
        {
            (_, offset) = ReadBinary(packet.Body, offset, "密码");
        }

        if (offset != packet.Body.Length)
        {
            throw new MqttException("MQTT CONNECT 载荷包含未解析的尾部字节。");
        }

        return new MqttConnectPacket(clientId, (flags & 0x02) != 0, keepAlive, username, willTopic, willPayload, willQos, willRetain);
    }

    public static MqttPublishPacket DecodePublish(MqttPacket packet)
    {
        if (packet.Type != MqttPacketType.Publish || packet.Body.Length < 2)
        {
            throw new MqttException("MQTT PUBLISH 报文长度不足。");
        }

        var qualityOfService = (MqttQualityOfService)((packet.Flags >> 1) & 0x03);
        if (qualityOfService > MqttQualityOfService.ExactlyOnce)
        {
            throw new MqttException("MQTT PUBLISH QoS 3 无效。");
        }

        var (topic, offset) = ReadUtf8(packet.Body, 0, "PUBLISH 主题");
        EnsureTopicName(topic);
        ushort? packetIdentifier = null;
        if (qualityOfService != MqttQualityOfService.AtMostOnce)
        {
            if (offset + 2 > packet.Body.Length)
            {
                throw new MqttException("MQTT QoS 1/2 PUBLISH 缺少报文标识符。");
            }

            packetIdentifier = BinaryPrimitives.ReadUInt16BigEndian(packet.Body.AsSpan(offset, 2));
            EnsurePacketIdentifier(packetIdentifier.Value);
            offset += 2;
        }

        return new MqttPublishPacket(
            topic,
            packet.Body[offset..],
            (packet.Flags & 1) != 0,
            (packet.Flags & 8) != 0,
            qualityOfService,
            packetIdentifier);
    }

    public static MqttSubscription DecodeSubscription(MqttPacket packet, out ushort packetIdentifier)
    {
        if (packet.Type != MqttPacketType.Subscribe)
        {
            throw new MqttException("报文不是 MQTT SUBSCRIBE。");
        }

        packetIdentifier = ReadPacketId(packet, "SUBSCRIBE");
        var (filter, offset) = ReadUtf8(packet.Body, 2, "SUBSCRIBE 主题过滤器");
        if (offset + 1 != packet.Body.Length)
        {
            throw new MqttException("当前 MQTT 客户端每个 SUBSCRIBE 报文只支持一个主题过滤器。");
        }

        var qualityOfService = (MqttQualityOfService)packet.Body[offset];
        ValidateQualityOfService(qualityOfService);
        EnsureTopicFilter(filter);
        return new MqttSubscription(filter, qualityOfService);
    }

    public static string DecodeUnsubscribe(MqttPacket packet, out ushort packetIdentifier)
    {
        if (packet.Type != MqttPacketType.Unsubscribe)
        {
            throw new MqttException("报文不是 MQTT UNSUBSCRIBE。");
        }

        packetIdentifier = ReadPacketId(packet, "UNSUBSCRIBE");
        var (filter, offset) = ReadUtf8(packet.Body, 2, "UNSUBSCRIBE 主题过滤器");
        if (offset != packet.Body.Length)
        {
            throw new MqttException("当前 MQTT 客户端每个 UNSUBSCRIBE 报文只支持一个主题过滤器。");
        }

        EnsureTopicFilter(filter);
        return filter;
    }

    public static ushort ReadPacketId(MqttPacket packet, string operation)
    {
        if (packet.Body.Length < 2)
        {
            throw new MqttException($"MQTT {operation} 响应缺少报文标识符。");
        }

        var packetId = BinaryPrimitives.ReadUInt16BigEndian(packet.Body.AsSpan(0, 2));
        EnsurePacketIdentifier(packetId);
        return packetId;
    }

    public static byte ReadReturnCode(MqttPacket packet, string operation)
    {
        if (packet.Body.Length < 1)
        {
            throw new MqttException($"MQTT {operation} 响应缺少返回码。");
        }

        return packet.Body[^1];
    }

    public static (string Value, int Offset) ReadUtf8(ReadOnlySpan<byte> body, int offset, string field)
    {
        var (bytes, nextOffset) = ReadBinary(body, offset, field);
        try
        {
            var value = StrictUtf8.GetString(bytes);
            if (value.Contains('\0'))
            {
                throw new MqttException($"MQTT {field} 包含空字符。");
            }

            return (value, nextOffset);
        }
        catch (DecoderFallbackException ex)
        {
            throw new MqttException($"MQTT {field} 不是合法 UTF-8：{ex.Message}");
        }
    }

    public static void EnsureUtf8(string value, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Contains('\0') || StrictUtf8.GetByteCount(value) > ushort.MaxValue)
        {
            throw new ZeusException($"MQTT {field} 不能为空、不能包含空字符且 UTF-8 长度不能超过 65535 字节。");
        }
    }

    public static void EnsureTopicName(string topic)
    {
        EnsureUtf8(topic, "主题");
        if (topic.Contains('#') || topic.Contains('+'))
        {
            throw new ZeusException("MQTT 发布主题不能包含 + 或 # 通配符。");
        }
    }

    public static void EnsureTopicFilter(string topic)
    {
        EnsureUtf8(topic, "主题过滤器");
        var levels = topic.Split('/');
        for (var i = 0; i < levels.Length; i++)
        {
            if (levels[i].Contains('+') && levels[i] != "+")
            {
                throw new ZeusException("MQTT + 通配符必须独占一个主题层级。");
            }

            if (levels[i].Contains('#') && (levels[i] != "#" || i != levels.Length - 1))
            {
                throw new ZeusException("MQTT # 通配符必须位于过滤器最后且独占一个主题层级。");
            }
        }
    }

    public static bool TopicMatches(string filter, string topic)
    {
        if (topic.StartsWith('$') && !filter.StartsWith('$'))
        {
            return false;
        }

        var filters = filter.Split('/');
        var topics = topic.Split('/');
        for (var i = 0; i < filters.Length; i++)
        {
            if (filters[i] == "#")
            {
                return true;
            }

            if (i >= topics.Length || (filters[i] != "+" && filters[i] != topics[i]))
            {
                return false;
            }
        }

        return filters.Length == topics.Length;
    }

    public static void ValidateOptions(MqttOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Username is null && options.Password is not null)
        {
            throw new ZeusException("MQTT Password 不能在未设置 Username 时单独使用。");
        }

        if ((options.WillTopic is null) != (options.WillPayload is null))
        {
            throw new ZeusException("MQTT WillTopic 与 WillPayload 必须同时设置或同时省略。");
        }

        if (options.WillTopic is not null)
        {
            EnsureTopicName(options.WillTopic);
            ValidateQualityOfService(options.WillQualityOfService);
        }
        else if (options.WillRetain || options.WillQualityOfService != MqttQualityOfService.AtMostOnce)
        {
            throw new ZeusException("未设置 MQTT 遗嘱时不能设置 WillRetain 或非零 WillQualityOfService。");
        }

        if (options.MaximumPacketSize is < 2 or > MaximumRemainingLength)
        {
            throw new ZeusException($"MQTT MaximumPacketSize 必须介于 2 与 {MaximumRemainingLength} 之间。");
        }

        if (options.ReconnectInitialDelay < TimeSpan.Zero || options.ReconnectMaxDelay < options.ReconnectInitialDelay)
        {
            throw new ZeusException("MQTT 自动重连等待时间必须满足 0 <= ReconnectInitialDelay <= ReconnectMaxDelay。");
        }

        if (!double.IsFinite(options.ReconnectBackoffMultiplier) || options.ReconnectBackoffMultiplier < 1)
        {
            throw new ZeusException("MQTT ReconnectBackoffMultiplier 必须是大于或等于 1 的有限数值。");
        }
    }

    public static void ValidateQualityOfService(MqttQualityOfService qualityOfService)
    {
        if (qualityOfService is < MqttQualityOfService.AtMostOnce or > MqttQualityOfService.ExactlyOnce)
        {
            throw new ZeusException($"MQTT QoS {(int)qualityOfService} 无效，可选 0、1、2。");
        }
    }

    private static (byte[] Value, int Offset) ReadBinary(ReadOnlySpan<byte> body, int offset, string field)
    {
        if (offset + 2 > body.Length)
        {
            throw new MqttException($"MQTT {field} 长度字段不足。");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(offset, 2));
        offset += 2;
        if (offset + length > body.Length)
        {
            throw new MqttException($"MQTT {field} 内容长度不足。");
        }

        return (body.Slice(offset, length).ToArray(), offset + length);
    }

    private static void ValidateFixedHeader(MqttPacketType type, byte flags)
    {
        if (type is < MqttPacketType.Connect or > MqttPacketType.Disconnect)
        {
            throw new MqttException($"MQTT 固定头类型 {(byte)type} 无效。");
        }

        var expected = type switch
        {
            MqttPacketType.Publish => flags,
            MqttPacketType.PubRel or MqttPacketType.Subscribe or MqttPacketType.Unsubscribe => (byte)2,
            _ => (byte)0
        };
        if (flags != expected || type == MqttPacketType.Publish && ((flags >> 1) & 0x03) == 3)
        {
            throw new MqttException($"MQTT {type} 固定头标志 0x{flags:X1} 无效。");
        }
    }

    private static void EnsurePacketIdentifier(ushort packetIdentifier)
    {
        if (packetIdentifier == 0)
        {
            throw new ZeusException("MQTT 报文标识符不能为 0。");
        }
    }

    private static void WriteUtf8(List<byte> destination, string value)
    {
        EnsureUtf8(value, "文本");
        WriteBinary(destination, StrictUtf8.GetBytes(value));
    }

    private static void WriteBinary(List<byte> destination, ReadOnlySpan<byte> value)
    {
        if (value.Length > ushort.MaxValue)
        {
            throw new ZeusException("MQTT 二进制字段长度不能超过 65535 字节。");
        }

        WriteUInt16(destination, (ushort)value.Length);
        destination.AddRange(value.ToArray());
    }

    private static void WriteUInt16(List<byte> destination, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        destination.AddRange(bytes.ToArray());
    }

    private static void WriteRemainingLength(List<byte> destination, int value)
    {
        do
        {
            var encoded = value % 128;
            value /= 128;
            if (value > 0)
            {
                encoded |= 0x80;
            }

            destination.Add((byte)encoded);
        }
        while (value > 0);
    }
}
