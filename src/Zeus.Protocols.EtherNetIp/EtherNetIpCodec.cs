using System.Globalization;
using System.Text;

namespace Zeus;

/// <summary>
/// EtherNet/IP 封装层与常用 CIP 载荷编解码。
/// </summary>
internal static class EtherNetIpCodec
{
    public const ushort RegisterSession = 0x0065;
    public const ushort UnregisterSession = 0x0066;
    public const ushort SendRRData = 0x006F;

    public const byte ServiceGetAttributeSingle = 0x0E;
    public const byte ServiceSetAttributeSingle = 0x10;
    public const byte ServiceReadTag = 0x4C;
    public const byte ServiceWriteTag = 0x4D;

    private const ushort CpfNullAddressItem = 0x0000;
    private const ushort CpfUnconnectedDataItem = 0x00B2;
    private const byte SymbolicSegment = 0x91;

    public static byte[] EncodeRegisterSession(ushort protocolVersion, ulong senderContext)
    {
        var body = new byte[4];
        WriteUInt16LittleEndian(body.AsSpan(0, 2), protocolVersion);
        WriteUInt16LittleEndian(body.AsSpan(2, 2), 0);
        return EncodePacket(RegisterSession, 0, senderContext, body);
    }

    public static byte[] EncodeUnregisterSession(uint sessionHandle, ulong senderContext)
        => EncodePacket(UnregisterSession, sessionHandle, senderContext, []);

    public static byte[] EncodeSendRRData(uint sessionHandle, ushort timeout, ulong senderContext, ReadOnlySpan<byte> cip)
    {
        var body = new byte[16 + cip.Length];
        WriteUInt32LittleEndian(body.AsSpan(0, 4), 0);
        WriteUInt16LittleEndian(body.AsSpan(4, 2), timeout);
        WriteUInt16LittleEndian(body.AsSpan(6, 2), 2);
        WriteUInt16LittleEndian(body.AsSpan(8, 2), CpfNullAddressItem);
        WriteUInt16LittleEndian(body.AsSpan(10, 2), 0);
        WriteUInt16LittleEndian(body.AsSpan(12, 2), CpfUnconnectedDataItem);
        WriteUInt16LittleEndian(body.AsSpan(14, 2), (ushort)cip.Length);
        cip.CopyTo(body.AsSpan(16));
        return EncodePacket(SendRRData, sessionHandle, senderContext, body);
    }

    public static byte[] EncodeSendRRDataResponse(uint sessionHandle, ulong senderContext, ReadOnlySpan<byte> cip)
        => EncodeSendRRData(sessionHandle, 0, senderContext, cip);

    public static byte[] EncodeCipRequest(byte service, ReadOnlySpan<byte> path, ReadOnlySpan<byte> data)
    {
        var normalizedPath = PadEven(path);
        var request = new byte[2 + normalizedPath.Length + data.Length];
        request[0] = service;
        request[1] = checked((byte)(normalizedPath.Length / 2));
        normalizedPath.CopyTo(request.AsSpan(2));
        data.CopyTo(request.AsSpan(2 + normalizedPath.Length));
        return request;
    }

    public static byte[] EncodeCipResponse(byte requestService, byte generalStatus, ReadOnlySpan<byte> data, IReadOnlyList<ushort>? additionalStatus = null)
    {
        var extra = additionalStatus?.Count ?? 0;
        var response = new byte[4 + extra * 2 + data.Length];
        response[0] = (byte)(requestService | 0x80);
        response[1] = 0;
        response[2] = generalStatus;
        response[3] = (byte)extra;
        for (var i = 0; i < extra; i++)
        {
            WriteUInt16LittleEndian(response.AsSpan(4 + i * 2, 2), additionalStatus![i]);
        }

        data.CopyTo(response.AsSpan(4 + extra * 2));
        return response;
    }

    public static byte[] BuildReadTagRequest(string tagName, ushort elementCount = 1)
    {
        EnsureCount(elementCount, "EtherNet/IP Read Tag");
        var data = new byte[2];
        WriteUInt16LittleEndian(data, elementCount);
        return EncodeCipRequest(ServiceReadTag, BuildSymbolPath(tagName), data);
    }

    public static byte[] BuildWriteTagRequest(string tagName, EtherNetIpDataType dataType, object value, double? scale = null, ushort elementCount = 1)
    {
        EnsureCount(elementCount, "EtherNet/IP Write Tag");
        var encoded = EncodeValue(dataType, value, scale);
        var data = new byte[4 + encoded.Length];
        WriteUInt16LittleEndian(data.AsSpan(0, 2), (ushort)dataType);
        WriteUInt16LittleEndian(data.AsSpan(2, 2), elementCount);
        encoded.CopyTo(data.AsSpan(4));
        return EncodeCipRequest(ServiceWriteTag, BuildSymbolPath(tagName), data);
    }

    public static byte[] BuildGetAttributeSingleRequest(ushort classId, uint instanceId, ushort attributeId)
        => EncodeCipRequest(ServiceGetAttributeSingle, BuildAttributePath(classId, instanceId, attributeId), []);

    public static byte[] BuildSetAttributeSingleRequest(ushort classId, uint instanceId, ushort attributeId, ReadOnlySpan<byte> data)
        => EncodeCipRequest(ServiceSetAttributeSingle, BuildAttributePath(classId, instanceId, attributeId), data);

    public static byte[] BuildSymbolPath(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new ZeusProtocolException("EtherNet/IP 标签名不能为空。");
        }

        var output = new List<byte>();
        foreach (var segment in tagName.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(segment);
            if (bytes.Length is < 1 or > byte.MaxValue)
            {
                throw new ZeusProtocolException($"EtherNet/IP 标签段 {segment} 长度必须在 1 到 255 字节之间。");
            }

            output.Add(SymbolicSegment);
            output.Add((byte)bytes.Length);
            output.AddRange(bytes);
            if (bytes.Length % 2 != 0)
            {
                output.Add(0);
            }
        }

        return output.ToArray();
    }

    public static string DecodeSymbolPath(ReadOnlySpan<byte> path)
    {
        var segments = new List<string>();
        var offset = 0;
        while (offset < path.Length)
        {
            if (path[offset] == 0)
            {
                break;
            }

            if (path[offset] != SymbolicSegment || offset + 2 > path.Length)
            {
                throw new ZeusProtocolException("EtherNet/IP CIP 标签路径不是 ANSI Extended Symbol 格式。");
            }

            var length = path[offset + 1];
            if (offset + 2 + length > path.Length)
            {
                throw new ZeusProtocolException("EtherNet/IP CIP 标签路径长度不足。");
            }

            segments.Add(Encoding.ASCII.GetString(path.Slice(offset + 2, length)));
            offset += 2 + length + (length % 2);
        }

        if (segments.Count == 0)
        {
            throw new ZeusProtocolException("EtherNet/IP CIP 标签路径为空。");
        }

        return string.Join('.', segments);
    }

    public static byte[] BuildAttributePath(ushort classId, uint instanceId, ushort attributeId)
    {
        var bytes = new List<byte>();
        AddLogicalSegment(bytes, 0x20, classId);
        AddLogicalSegment(bytes, 0x24, instanceId);
        AddLogicalSegment(bytes, 0x30, attributeId);
        return bytes.ToArray();
    }

    public static (ushort ClassId, uint InstanceId, ushort AttributeId) DecodeAttributePath(ReadOnlySpan<byte> path)
    {
        var offset = 0;
        var classId = ReadLogicalSegment(path, ref offset, 0x20);
        var instanceId = ReadLogicalSegment(path, ref offset, 0x24);
        var attributeId = ReadLogicalSegment(path, ref offset, 0x30);
        return ((ushort)classId, instanceId, (ushort)attributeId);
    }

    public static object DecodeTagReadResponse(ReadOnlySpan<byte> data, EtherNetIpDataType expectedType, double? scale = null, ushort elementCount = 1)
    {
        if (data.Length < 2)
        {
            throw new ZeusProtocolException("EtherNet/IP Read Tag 响应长度不足。");
        }

        var actualType = (EtherNetIpDataType)ReadUInt16LittleEndian(data.Slice(0, 2));
        if (actualType != expectedType)
        {
            throw new ZeusProtocolException($"EtherNet/IP 标签类型为 {actualType}，期望 {expectedType}。");
        }

        return DecodeValue(expectedType, data.Slice(2), scale, elementCount);
    }

    public static byte[] EncodeTagReadResponse(EtherNetIpDataType dataType, object value)
    {
        var encoded = EncodeValue(dataType, value, null);
        var data = new byte[2 + encoded.Length];
        WriteUInt16LittleEndian(data.AsSpan(0, 2), (ushort)dataType);
        encoded.CopyTo(data.AsSpan(2));
        return data;
    }

    public static object DecodeValue(EtherNetIpDataType dataType, ReadOnlySpan<byte> data, double? scale = null, ushort elementCount = 1)
    {
        if (elementCount == 1)
        {
            var raw = DecodeScalar(dataType, data);
            return ApplyScale(raw, scale);
        }

        var size = GetByteLength(dataType);
        if (data.Length < size * elementCount)
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 数组响应长度不足。");
        }

        var result = new object[elementCount];
        for (var i = 0; i < elementCount; i++)
        {
            result[i] = ApplyScale(DecodeScalar(dataType, data.Slice(i * size, size)), scale);
        }

        return result;
    }

    public static byte[] EncodeValue(EtherNetIpDataType dataType, object value, double? scale = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        var actual = scale is { } factor
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture) / factor
            : value;
        var buffer = new byte[GetByteLength(dataType)];
        switch (dataType)
        {
            case EtherNetIpDataType.Bool:
                buffer[0] = ConvertToBoolean(actual) ? (byte)1 : (byte)0;
                break;
            case EtherNetIpDataType.SInt:
                buffer[0] = unchecked((byte)ConvertToSByte(actual, dataType));
                break;
            case EtherNetIpDataType.Int:
                WriteUInt16LittleEndian(buffer, unchecked((ushort)ConvertToInt16(actual, dataType)));
                break;
            case EtherNetIpDataType.DInt:
                WriteUInt32LittleEndian(buffer, unchecked((uint)ConvertToInt32(actual, dataType)));
                break;
            case EtherNetIpDataType.LInt:
                WriteUInt64LittleEndian(buffer, unchecked((ulong)ConvertToInt64(actual, dataType)));
                break;
            case EtherNetIpDataType.USInt:
                buffer[0] = ConvertToByte(actual, dataType);
                break;
            case EtherNetIpDataType.UInt:
                WriteUInt16LittleEndian(buffer, ConvertToUInt16(actual, dataType));
                break;
            case EtherNetIpDataType.UDInt:
                WriteUInt32LittleEndian(buffer, ConvertToUInt32(actual, dataType));
                break;
            case EtherNetIpDataType.ULInt:
                WriteUInt64LittleEndian(buffer, ConvertToUInt64(actual, dataType));
                break;
            case EtherNetIpDataType.Real:
                WriteUInt32LittleEndian(buffer, unchecked((uint)BitConverter.SingleToInt32Bits(ConvertToSingle(actual, dataType))));
                break;
            case EtherNetIpDataType.LReal:
                WriteUInt64LittleEndian(buffer, unchecked((ulong)BitConverter.DoubleToInt64Bits(ConvertToDouble(actual, dataType))));
                break;
            default:
                throw new ZeusProtocolException($"不支持的 EtherNet/IP 数据类型：{dataType}。");
        }

        return buffer;
    }

    public static bool TryDecodePacket(IReadOnlyList<byte> buffer, out EtherNetIpPacket packet, out int consumed)
    {
        packet = default;
        consumed = 0;
        if (buffer.Count < 24)
        {
            return false;
        }

        var length = ReadUInt16LittleEndian(buffer, 2);
        var total = 24 + length;
        if (buffer.Count < total)
        {
            return false;
        }

        var command = ReadUInt16LittleEndian(buffer, 0);
        var sessionHandle = ReadUInt32LittleEndian(buffer, 4);
        var status = ReadUInt32LittleEndian(buffer, 8);
        var senderContext = ReadUInt64LittleEndian(buffer, 12);
        var data = Copy(buffer, 24, length);
        packet = new EtherNetIpPacket(command, sessionHandle, status, senderContext, data);
        consumed = total;
        return true;
    }

    public static uint DecodeRegisterSession(ReadOnlySpan<byte> data, EtherNetIpPacket packet)
    {
        if (data.Length < 4)
        {
            throw new ZeusProtocolException("EtherNet/IP Register Session 响应长度不足。");
        }

        return packet.SessionHandle;
    }

    public static byte[] DecodeSendRRData(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            throw new ZeusProtocolException("EtherNet/IP SendRRData 响应长度不足。");
        }

        var itemCount = ReadUInt16LittleEndian(data.Slice(6, 2));
        var offset = 8;
        for (var i = 0; i < itemCount; i++)
        {
            if (offset + 4 > data.Length)
            {
                throw new ZeusProtocolException("EtherNet/IP CPF Item 长度不足。");
            }

            var itemType = ReadUInt16LittleEndian(data.Slice(offset, 2));
            var itemLength = ReadUInt16LittleEndian(data.Slice(offset + 2, 2));
            offset += 4;
            if (offset + itemLength > data.Length)
            {
                throw new ZeusProtocolException("EtherNet/IP CPF Item 数据长度不足。");
            }

            if (itemType == CpfUnconnectedDataItem)
            {
                return data.Slice(offset, itemLength).ToArray();
            }

            offset += itemLength;
        }

        throw new ZeusProtocolException("EtherNet/IP SendRRData 响应缺少 Unconnected Data Item。");
    }

    public static byte[] DecodeCipResponse(byte expectedRequestService, ReadOnlySpan<byte> response)
    {
        if (response.Length < 4)
        {
            throw new ZeusProtocolException("CIP 响应长度不足。");
        }

        var expectedReply = (byte)(expectedRequestService | 0x80);
        if (response[0] != expectedReply)
        {
            throw new ZeusProtocolException($"CIP 响应服务码为 0x{response[0]:X2}，期望 0x{expectedReply:X2}。");
        }

        var generalStatus = response[2];
        var additionalCount = response[3];
        var dataOffset = 4 + additionalCount * 2;
        if (response.Length < dataOffset)
        {
            throw new ZeusProtocolException("CIP 附加状态长度不足。");
        }

        var additional = new ushort[additionalCount];
        for (var i = 0; i < additionalCount; i++)
        {
            additional[i] = ReadUInt16LittleEndian(response.Slice(4 + i * 2, 2));
        }

        if (generalStatus != 0)
        {
            var suffix = additional.Length == 0
                ? string.Empty
                : $"，附加状态 {string.Join(", ", additional.Select(item => "0x" + item.ToString("X4", CultureInfo.InvariantCulture)))}";
            throw new EtherNetIpException($"CIP 服务 0x{expectedRequestService:X2} 返回状态 0x{generalStatus:X2}{suffix}。", generalStatus: generalStatus, additionalStatus: additional);
        }

        return response[dataOffset..].ToArray();
    }

    public static (byte Service, byte[] Path, byte[] Data) DecodeCipRequest(ReadOnlySpan<byte> request)
    {
        if (request.Length < 2)
        {
            throw new ZeusProtocolException("CIP 请求长度不足。");
        }

        var pathLength = request[1] * 2;
        if (request.Length < 2 + pathLength)
        {
            throw new ZeusProtocolException("CIP 请求路径长度不足。");
        }

        return (request[0], request.Slice(2, pathLength).ToArray(), request[(2 + pathLength)..].ToArray());
    }

    public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> data)
        => (ushort)(data[0] | (data[1] << 8));

    public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> data)
        => (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));

    public static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> data)
        => ReadUInt32LittleEndian(data) | ((ulong)ReadUInt32LittleEndian(data.Slice(4, 4)) << 32);

    public static void WriteUInt16LittleEndian(Span<byte> destination, ushort value)
    {
        destination[0] = (byte)(value & 0xFF);
        destination[1] = (byte)(value >> 8);
    }

    public static void WriteUInt32LittleEndian(Span<byte> destination, uint value)
    {
        destination[0] = (byte)(value & 0xFF);
        destination[1] = (byte)((value >> 8) & 0xFF);
        destination[2] = (byte)((value >> 16) & 0xFF);
        destination[3] = (byte)(value >> 24);
    }

    public static void WriteUInt64LittleEndian(Span<byte> destination, ulong value)
    {
        WriteUInt32LittleEndian(destination, (uint)(value & 0xFFFFFFFF));
        WriteUInt32LittleEndian(destination.Slice(4, 4), (uint)(value >> 32));
    }

    private static byte[] EncodePacket(ushort command, uint sessionHandle, ulong senderContext, ReadOnlySpan<byte> data)
    {
        if (data.Length > ushort.MaxValue)
        {
            throw new ZeusProtocolException($"EtherNet/IP 封装载荷过长：{data.Length}。");
        }

        var packet = new byte[24 + data.Length];
        WriteUInt16LittleEndian(packet.AsSpan(0, 2), command);
        WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)data.Length);
        WriteUInt32LittleEndian(packet.AsSpan(4, 4), sessionHandle);
        WriteUInt32LittleEndian(packet.AsSpan(8, 4), 0);
        WriteUInt64LittleEndian(packet.AsSpan(12, 8), senderContext);
        WriteUInt32LittleEndian(packet.AsSpan(20, 4), 0);
        data.CopyTo(packet.AsSpan(24));
        return packet;
    }

    private static ushort ReadUInt16LittleEndian(IReadOnlyList<byte> data, int offset)
        => (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadUInt32LittleEndian(IReadOnlyList<byte> data, int offset)
        => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static ulong ReadUInt64LittleEndian(IReadOnlyList<byte> data, int offset)
        => ReadUInt32LittleEndian(data, offset) | ((ulong)ReadUInt32LittleEndian(data, offset + 4) << 32);

    private static void AddLogicalSegment(List<byte> bytes, byte baseCode, uint value)
    {
        if (value <= byte.MaxValue)
        {
            bytes.Add(baseCode);
            bytes.Add((byte)value);
            return;
        }

        if (value <= ushort.MaxValue)
        {
            bytes.Add((byte)(baseCode | 0x01));
            bytes.Add(0);
            var temp = new byte[2];
            WriteUInt16LittleEndian(temp, (ushort)value);
            bytes.AddRange(temp);
            return;
        }

        bytes.Add((byte)(baseCode | 0x02));
        bytes.Add(0);
        var wide = new byte[4];
        WriteUInt32LittleEndian(wide, value);
        bytes.AddRange(wide);
    }

    private static uint ReadLogicalSegment(ReadOnlySpan<byte> path, ref int offset, byte baseCode)
    {
        if (offset + 2 > path.Length)
        {
            throw new ZeusProtocolException("EtherNet/IP CIP 逻辑路径长度不足。");
        }

        var segment = path[offset++];
        if ((segment & 0xFC) != baseCode)
        {
            throw new ZeusProtocolException($"EtherNet/IP CIP 逻辑路径段 0x{segment:X2} 与期望 0x{baseCode:X2} 不匹配。");
        }

        var format = segment & 0x03;
        _ = path[offset++];
        if (format == 0)
        {
            return path[offset - 1];
        }

        if (format == 1 && offset + 2 <= path.Length)
        {
            var value = ReadUInt16LittleEndian(path.Slice(offset, 2));
            offset += 2;
            return value;
        }

        if (format == 2 && offset + 4 <= path.Length)
        {
            var value = ReadUInt32LittleEndian(path.Slice(offset, 4));
            offset += 4;
            return value;
        }

        throw new ZeusProtocolException("EtherNet/IP CIP 逻辑路径长度不足或格式不受支持。");
    }

    private static byte[] PadEven(ReadOnlySpan<byte> value)
    {
        if (value.Length % 2 == 0)
        {
            return value.ToArray();
        }

        var result = new byte[value.Length + 1];
        value.CopyTo(result);
        return result;
    }

    private static int GetByteLength(EtherNetIpDataType dataType)
        => dataType switch
        {
            EtherNetIpDataType.Bool or EtherNetIpDataType.SInt or EtherNetIpDataType.USInt => 1,
            EtherNetIpDataType.Int or EtherNetIpDataType.UInt => 2,
            EtherNetIpDataType.DInt or EtherNetIpDataType.UDInt or EtherNetIpDataType.Real => 4,
            EtherNetIpDataType.LInt or EtherNetIpDataType.ULInt or EtherNetIpDataType.LReal => 8,
            _ => throw new ZeusProtocolException($"不支持的 EtherNet/IP 数据类型：{dataType}。")
        };

    private static object DecodeScalar(EtherNetIpDataType dataType, ReadOnlySpan<byte> data)
    {
        var size = GetByteLength(dataType);
        if (data.Length < size)
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 响应长度不足。");
        }

        return dataType switch
        {
            EtherNetIpDataType.Bool => data[0] != 0,
            EtherNetIpDataType.SInt => unchecked((sbyte)data[0]),
            EtherNetIpDataType.Int => unchecked((short)ReadUInt16LittleEndian(data)),
            EtherNetIpDataType.DInt => unchecked((int)ReadUInt32LittleEndian(data)),
            EtherNetIpDataType.LInt => unchecked((long)ReadUInt64LittleEndian(data)),
            EtherNetIpDataType.USInt => data[0],
            EtherNetIpDataType.UInt => ReadUInt16LittleEndian(data),
            EtherNetIpDataType.UDInt => ReadUInt32LittleEndian(data),
            EtherNetIpDataType.ULInt => ReadUInt64LittleEndian(data),
            EtherNetIpDataType.Real => BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32LittleEndian(data))),
            EtherNetIpDataType.LReal => BitConverter.Int64BitsToDouble(unchecked((long)ReadUInt64LittleEndian(data))),
            _ => throw new ZeusProtocolException($"不支持的 EtherNet/IP 数据类型：{dataType}。")
        };
    }

    private static object ApplyScale(object raw, double? scale)
    {
        if (scale is null || raw is bool)
        {
            return raw;
        }

        return Convert.ToDouble(raw, CultureInfo.InvariantCulture) * scale.Value;
    }

    private static bool ConvertToBoolean(object value)
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

        return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    private static sbyte ConvertToSByte(object value, EtherNetIpDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < sbyte.MinValue or > sbyte.MaxValue)
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 写入值 {value} 超出 SByte 范围。");
        }

        return (sbyte)number;
    }

    private static short ConvertToInt16(object value, EtherNetIpDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < short.MinValue or > short.MaxValue)
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 写入值 {value} 超出 Int16 范围。");
        }

        return (short)number;
    }

    private static int ConvertToInt32(object value, EtherNetIpDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < int.MinValue or > int.MaxValue)
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 写入值 {value} 超出 Int32 范围。");
        }

        return (int)number;
    }

    private static long ConvertToInt64(object value, EtherNetIpDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < long.MinValue or > long.MaxValue)
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 写入值 {value} 超出 Int64 范围。");
        }

        return (long)number;
    }

    private static byte ConvertToByte(object value, EtherNetIpDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < byte.MinValue or > byte.MaxValue)
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 写入值 {value} 超出 Byte 范围。");
        }

        return (byte)number;
    }

    private static ushort ConvertToUInt16(object value, EtherNetIpDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < ushort.MinValue or > ushort.MaxValue)
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 写入值 {value} 超出 UInt16 范围。");
        }

        return (ushort)number;
    }

    private static uint ConvertToUInt32(object value, EtherNetIpDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < uint.MinValue or > uint.MaxValue)
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 写入值 {value} 超出 UInt32 范围。");
        }

        return (uint)number;
    }

    private static ulong ConvertToUInt64(object value, EtherNetIpDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < ulong.MinValue or > ulong.MaxValue)
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 写入值 {value} 超出 UInt64 范围。");
        }

        return (ulong)number;
    }

    private static float ConvertToSingle(object value, EtherNetIpDataType dataType)
    {
        var number = Convert.ToSingle(value, CultureInfo.InvariantCulture);
        if (!float.IsFinite(number))
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 写入值必须是有限数值。");
        }

        return number;
    }

    private static double ConvertToDouble(object value, EtherNetIpDataType dataType)
    {
        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number))
        {
            throw new ZeusProtocolException($"EtherNet/IP {dataType} 写入值必须是有限数值。");
        }

        return number;
    }

    private static void EnsureCount(int count, string operation)
    {
        if (count is < 1 or > ushort.MaxValue)
        {
            throw new ZeusProtocolException($"{operation} 元素数量必须在 1 到 65535 之间，当前为 {count}。");
        }
    }

    private static byte[] Copy(IReadOnlyList<byte> buffer, int offset, int count)
    {
        var result = new byte[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = buffer[offset + i];
        }

        return result;
    }
}

internal readonly record struct EtherNetIpPacket(ushort Command, uint SessionHandle, uint Status, ulong SenderContext, byte[] Data);
