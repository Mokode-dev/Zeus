namespace Zeus;

/// <summary>
/// Omron FINS UDP/TCP 帧编解码与常用命令载荷构造。
/// </summary>
internal static class FinsCodec
{
    public const ushort MemoryAreaRead = 0x0101;
    public const ushort MemoryAreaWrite = 0x0102;
    public const ushort MemoryAreaFill = 0x0103;
    public const ushort MultipleMemoryAreaRead = 0x0104;

    public const uint TcpCommandNodeAddressDataSend = 0x00000000;
    public const uint TcpCommandNodeAddressDataSendResponse = 0x00000001;
    public const uint TcpCommandFinsFrameSend = 0x00000002;
    public const uint TcpCommandFinsFrameSendErrorNotification = 0x00000003;

    private static readonly byte[] TcpMagic = [(byte)'F', (byte)'I', (byte)'N', (byte)'S'];

    public static byte[] EncodeRequestFrame(FinsOptions options, byte sid, ushort command, ReadOnlySpan<byte> data)
    {
        var frame = new byte[12 + data.Length];
        frame[0] = options.InformationControlField;
        frame[1] = 0x00;
        frame[2] = options.GatewayCount;
        frame[3] = options.DestinationNetwork;
        frame[4] = options.DestinationNode;
        frame[5] = options.DestinationUnit;
        frame[6] = options.SourceNetwork;
        frame[7] = options.SourceNode;
        frame[8] = options.SourceUnit;
        frame[9] = sid;
        WriteUInt16BigEndian(frame.AsSpan(10, 2), command);
        data.CopyTo(frame.AsSpan(12));
        return frame;
    }

    public static byte[] EncodeResponseFrame(FinsRequestContext context, ushort endCode, ReadOnlySpan<byte> data)
    {
        var frame = new byte[14 + data.Length];
        frame[0] = 0xC0;
        frame[1] = 0x00;
        frame[2] = context.GatewayCount;
        frame[3] = context.SourceNetwork;
        frame[4] = context.SourceNode;
        frame[5] = context.SourceUnit;
        frame[6] = context.DestinationNetwork;
        frame[7] = context.DestinationNode;
        frame[8] = context.DestinationUnit;
        frame[9] = context.ServiceId;
        WriteUInt16BigEndian(frame.AsSpan(10, 2), context.Command);
        WriteUInt16BigEndian(frame.AsSpan(12, 2), endCode);
        data.CopyTo(frame.AsSpan(14));
        return frame;
    }

    public static bool TryDecodeRequestFrame(
        ReadOnlySpan<byte> frame,
        out FinsRequestContext context,
        out byte[] data)
    {
        context = default;
        data = [];
        if (frame.Length < 12)
        {
            return false;
        }

        context = new FinsRequestContext(
            frame[2],
            frame[3],
            frame[4],
            frame[5],
            frame[6],
            frame[7],
            frame[8],
            frame[9],
            ReadUInt16BigEndian(frame.Slice(10, 2)));
        data = frame[12..].ToArray();
        return true;
    }

    public static bool TryDecodeResponse(
        IReadOnlyList<byte> buffer,
        FinsTransport transport,
        out FinsResponseFrame response,
        out int consumed)
    {
        response = default;
        consumed = 0;
        byte[] frame;
        if (transport == FinsTransport.Tcp)
        {
            if (!TryDecodeTcpPacket(buffer, out var tcpCommand, out var tcpError, out var payload, out consumed))
            {
                return false;
            }

            if (tcpError != 0)
            {
                throw new ZeusProtocolException($"FINS/TCP 返回错误码 0x{tcpError:X8}。");
            }

            if (tcpCommand == TcpCommandFinsFrameSendErrorNotification)
            {
                throw new ZeusProtocolException("FINS/TCP 收到 FINS frame send error notification。请检查节点地址与 PLC 路由设置。");
            }

            if (tcpCommand != TcpCommandFinsFrameSend)
            {
                throw new ZeusProtocolException($"FINS/TCP 响应命令 0x{tcpCommand:X8} 不是 FINS frame send。");
            }

            frame = payload;
        }
        else
        {
            if (buffer.Count < 14)
            {
                return false;
            }

            frame = Copy(buffer, 0, buffer.Count);
            consumed = buffer.Count;
        }

        if (frame.Length < 14)
        {
            throw new ZeusProtocolException("FINS 响应帧长度不足。");
        }

        response = new FinsResponseFrame(
            frame[9],
            ReadUInt16BigEndian(frame.AsSpan(10, 2)),
            ReadUInt16BigEndian(frame.AsSpan(12, 2)),
            frame[14..]);
        return true;
    }

    public static byte[] EncodeTcpPacket(uint command, uint errorCode, ReadOnlySpan<byte> data)
    {
        var length = checked((uint)(8 + data.Length));
        var packet = new byte[16 + data.Length];
        TcpMagic.CopyTo(packet, 0);
        WriteUInt32BigEndian(packet.AsSpan(4, 4), length);
        WriteUInt32BigEndian(packet.AsSpan(8, 4), command);
        WriteUInt32BigEndian(packet.AsSpan(12, 4), errorCode);
        data.CopyTo(packet.AsSpan(16));
        return packet;
    }

    public static bool TryDecodeTcpPacket(
        IReadOnlyList<byte> buffer,
        out uint command,
        out uint errorCode,
        out byte[] payload,
        out int consumed)
    {
        command = 0;
        errorCode = 0;
        payload = [];
        consumed = 0;
        if (buffer.Count < 16)
        {
            return false;
        }

        for (var i = 0; i < TcpMagic.Length; i++)
        {
            if (buffer[i] != TcpMagic[i])
            {
                throw new ZeusProtocolException("FINS/TCP 响应头不是 ASCII 'FINS'。请确认通道连接的是 FINS/TCP 服务。");
            }
        }

        var length = ReadUInt32BigEndian(buffer, 4);
        if (length < 8 || length > int.MaxValue - 8)
        {
            throw new ZeusProtocolException($"FINS/TCP 长度字段异常：{length}。");
        }

        var total = checked((int)(8 + length));
        if (buffer.Count < total)
        {
            return false;
        }

        command = ReadUInt32BigEndian(buffer, 8);
        errorCode = ReadUInt32BigEndian(buffer, 12);
        payload = Copy(buffer, 16, total - 16);
        consumed = total;
        return true;
    }

    public static byte[] BuildTcpNodeAddressRequest(byte requestedClientNode)
    {
        var data = new byte[4];
        WriteUInt32BigEndian(data, requestedClientNode);
        return EncodeTcpPacket(TcpCommandNodeAddressDataSend, 0, data);
    }

    public static (byte ClientNode, byte ServerNode) DecodeTcpNodeAddressResponse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            throw new ZeusProtocolException("FINS/TCP 节点地址响应长度不足。");
        }

        return ((byte)ReadUInt32BigEndian(payload.Slice(0, 4)), (byte)ReadUInt32BigEndian(payload.Slice(4, 4)));
    }

    public static byte[] BuildMemoryAreaReadRequest(FinsMemoryAreaCode area, ushort address, byte bitOffset, ushort count)
    {
        EnsureCount(count, "FINS Memory Area Read");
        ValidateAreaKind(area, area.Kind);
        var data = new byte[6];
        data[0] = area.Code;
        WriteUInt16BigEndian(data.AsSpan(1, 2), address);
        data[3] = bitOffset;
        WriteUInt16BigEndian(data.AsSpan(4, 2), count);
        return data;
    }

    public static byte[] BuildMemoryAreaWriteRequest(
        FinsMemoryAreaCode area,
        ushort address,
        byte bitOffset,
        IReadOnlyList<ushort> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        ValidateAreaKind(area, FinsMemoryAreaKind.Word);
        EnsureCount(words.Count, "FINS Memory Area Write");
        var data = new byte[6 + (words.Count * 2)];
        data[0] = area.Code;
        WriteUInt16BigEndian(data.AsSpan(1, 2), address);
        data[3] = bitOffset;
        WriteUInt16BigEndian(data.AsSpan(4, 2), (ushort)words.Count);
        for (var i = 0; i < words.Count; i++)
        {
            WriteUInt16BigEndian(data.AsSpan(6 + (i * 2), 2), words[i]);
        }

        return data;
    }

    public static byte[] BuildMemoryAreaWriteRequest(
        FinsMemoryAreaCode area,
        ushort address,
        byte bitOffset,
        IReadOnlyList<bool> bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        ValidateAreaKind(area, FinsMemoryAreaKind.Bit);
        EnsureCount(bits.Count, "FINS Memory Area Write");
        var data = new byte[6 + bits.Count];
        data[0] = area.Code;
        WriteUInt16BigEndian(data.AsSpan(1, 2), address);
        data[3] = bitOffset;
        WriteUInt16BigEndian(data.AsSpan(4, 2), (ushort)bits.Count);
        for (var i = 0; i < bits.Count; i++)
        {
            data[6 + i] = bits[i] ? (byte)1 : (byte)0;
        }

        return data;
    }

    public static byte[] BuildMemoryAreaFillRequest(FinsMemoryAreaCode area, ushort address, byte bitOffset, ushort count, ushort value)
    {
        ValidateAreaKind(area, FinsMemoryAreaKind.Word);
        EnsureCount(count, "FINS Memory Area Fill");
        var data = new byte[8];
        data[0] = area.Code;
        WriteUInt16BigEndian(data.AsSpan(1, 2), address);
        data[3] = bitOffset;
        WriteUInt16BigEndian(data.AsSpan(4, 2), count);
        WriteUInt16BigEndian(data.AsSpan(6, 2), value);
        return data;
    }

    public static byte[] BuildMultipleMemoryAreaReadRequest(IReadOnlyList<FinsMemoryAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        if (addresses.Count is < 1 or > 255)
        {
            throw new ZeusProtocolException($"FINS Multiple Memory Area Read 地址数量必须在 1 到 255 之间，当前为 {addresses.Count}。");
        }

        var data = new byte[addresses.Count * 4];
        for (var i = 0; i < addresses.Count; i++)
        {
            var offset = i * 4;
            data[offset] = addresses[i].Area.Code;
            WriteUInt16BigEndian(data.AsSpan(offset + 1, 2), addresses[i].WordAddress);
            data[offset + 3] = addresses[i].BitOffset;
        }

        return data;
    }

    public static ushort[] DecodeWordRead(ReadOnlySpan<byte> data, int count)
    {
        if (data.Length < count * 2)
        {
            throw new ZeusProtocolException("FINS 读字响应长度不足。");
        }

        var values = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadUInt16BigEndian(data.Slice(i * 2, 2));
        }

        return values;
    }

    public static bool[] DecodeBitRead(ReadOnlySpan<byte> data, int count)
    {
        if (data.Length < count)
        {
            throw new ZeusProtocolException("FINS 读位响应长度不足。");
        }

        var values = new bool[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = data[i] != 0;
        }

        return values;
    }

    public static FinsMemoryValue[] DecodeMultipleRead(ReadOnlySpan<byte> data, IReadOnlyList<FinsMemoryAddress> addresses)
    {
        var result = new FinsMemoryValue[addresses.Count];
        var offset = 0;
        for (var i = 0; i < addresses.Count; i++)
        {
            var address = addresses[i];
            if (address.Area.IsBit)
            {
                if (offset >= data.Length)
                {
                    throw new ZeusProtocolException("FINS 多点读取位响应长度不足。");
                }

                result[i] = new FinsMemoryValue(address, data[offset] != 0, null);
                offset++;
                continue;
            }

            if (offset + 2 > data.Length)
            {
                throw new ZeusProtocolException("FINS 多点读取字响应长度不足。");
            }

            result[i] = new FinsMemoryValue(address, null, ReadUInt16BigEndian(data.Slice(offset, 2)));
            offset += 2;
        }

        return result;
    }

    public static int GetWordCount(FinsDataType dataType)
        => dataType switch
        {
            FinsDataType.Bit => 0,
            FinsDataType.Word or FinsDataType.Int16 => 1,
            FinsDataType.UInt32 or FinsDataType.Int32 or FinsDataType.Real => 2,
            _ => throw new ZeusProtocolException($"不支持的 FINS 数据类型：{dataType}。")
        };

    public static object DecodeValue(FinsDataType dataType, IReadOnlyList<ushort> words, double? scale, FinsWordOrder wordOrder)
    {
        if (dataType == FinsDataType.Bit)
        {
            throw new ZeusProtocolException("FINS Bit 值不能从字数组解码。");
        }

        if (words.Count < GetWordCount(dataType))
        {
            throw new ZeusProtocolException($"FINS {dataType} 解码需要 {GetWordCount(dataType)} 个字。");
        }

        object raw = dataType switch
        {
            FinsDataType.Word => words[0],
            FinsDataType.Int16 => unchecked((short)words[0]),
            FinsDataType.UInt32 => CombineUInt32(words[0], words[1], wordOrder),
            FinsDataType.Int32 => unchecked((int)CombineUInt32(words[0], words[1], wordOrder)),
            FinsDataType.Real => BitConverter.Int32BitsToSingle(unchecked((int)CombineUInt32(words[0], words[1], wordOrder))),
            _ => throw new ZeusProtocolException($"不支持的 FINS 数据类型：{dataType}。")
        };

        if (scale is null)
        {
            return raw;
        }

        var number = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
        return number * scale.Value;
    }

    public static ushort[] EncodeValue(FinsDataType dataType, object value, double? scale, FinsWordOrder wordOrder)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (dataType == FinsDataType.Bit)
        {
            throw new ZeusProtocolException("FINS Bit 值不能编码为字数组。");
        }

        var actual = scale is { } factor
            ? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) / factor
            : value;

        return dataType switch
        {
            FinsDataType.Word => [ConvertToUInt16(actual, dataType)],
            FinsDataType.Int16 => [unchecked((ushort)ConvertToInt16(actual, dataType))],
            FinsDataType.UInt32 => SplitUInt32(ConvertToUInt32(actual, dataType), wordOrder),
            FinsDataType.Int32 => SplitUInt32(unchecked((uint)ConvertToInt32(actual, dataType)), wordOrder),
            FinsDataType.Real => SplitUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(ConvertToSingle(actual, dataType))), wordOrder),
            _ => throw new ZeusProtocolException($"不支持的 FINS 数据类型：{dataType}。")
        };
    }

    public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> data)
        => (ushort)((data[0] << 8) | data[1]);

    public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> data)
        => ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];

    public static void WriteUInt16BigEndian(Span<byte> destination, ushort value)
    {
        destination[0] = (byte)(value >> 8);
        destination[1] = (byte)(value & 0xFF);
    }

    public static void WriteUInt32BigEndian(Span<byte> destination, uint value)
    {
        destination[0] = (byte)(value >> 24);
        destination[1] = (byte)((value >> 16) & 0xFF);
        destination[2] = (byte)((value >> 8) & 0xFF);
        destination[3] = (byte)(value & 0xFF);
    }

    private static uint ReadUInt32BigEndian(IReadOnlyList<byte> data, int offset)
        => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static uint CombineUInt32(ushort first, ushort second, FinsWordOrder wordOrder)
        => wordOrder == FinsWordOrder.HighWordFirst
            ? ((uint)first << 16) | second
            : ((uint)second << 16) | first;

    private static ushort[] SplitUInt32(uint value, FinsWordOrder wordOrder)
    {
        var high = (ushort)(value >> 16);
        var low = (ushort)(value & 0xFFFF);
        return wordOrder == FinsWordOrder.HighWordFirst ? [high, low] : [low, high];
    }

    private static ushort ConvertToUInt16(object value, FinsDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < ushort.MinValue or > ushort.MaxValue)
        {
            throw new ZeusProtocolException($"FINS {dataType} 写入值 {value} 超出 UInt16 范围。");
        }

        return (ushort)number;
    }

    private static short ConvertToInt16(object value, FinsDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < short.MinValue or > short.MaxValue)
        {
            throw new ZeusProtocolException($"FINS {dataType} 写入值 {value} 超出 Int16 范围。");
        }

        return (short)number;
    }

    private static uint ConvertToUInt32(object value, FinsDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < uint.MinValue or > uint.MaxValue)
        {
            throw new ZeusProtocolException($"FINS {dataType} 写入值 {value} 超出 UInt32 范围。");
        }

        return (uint)number;
    }

    private static int ConvertToInt32(object value, FinsDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < int.MinValue or > int.MaxValue)
        {
            throw new ZeusProtocolException($"FINS {dataType} 写入值 {value} 超出 Int32 范围。");
        }

        return (int)number;
    }

    private static float ConvertToSingle(object value, FinsDataType dataType)
    {
        var number = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        if (!float.IsFinite(number))
        {
            throw new ZeusProtocolException($"FINS {dataType} 写入值必须是有限数值。");
        }

        return number;
    }

    private static void ValidateAreaKind(FinsMemoryAreaCode area, FinsMemoryAreaKind expected)
    {
        if (area.Kind != expected)
        {
            throw new ZeusProtocolException($"FINS 内存区 {area} 不是 {expected} 区。");
        }
    }

    private static void EnsureCount(int count, string operation)
    {
        if (count is < 1 or > 999)
        {
            throw new ZeusProtocolException($"{operation} 项目数量必须在 1 到 999 之间，当前为 {count}。");
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

internal readonly record struct FinsRequestContext(
    byte GatewayCount,
    byte DestinationNetwork,
    byte DestinationNode,
    byte DestinationUnit,
    byte SourceNetwork,
    byte SourceNode,
    byte SourceUnit,
    byte ServiceId,
    ushort Command);

internal readonly record struct FinsResponseFrame(byte ServiceId, ushort Command, ushort EndCode, byte[] Data);
