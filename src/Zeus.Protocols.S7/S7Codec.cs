namespace Zeus;

/// <summary>
/// Siemens S7 ISO-on-TCP 帧编解码。
/// </summary>
internal static class S7Codec
{
    private const byte TpktVersion = 0x03;
    private const byte CotpData = 0xF0;
    private const byte CotpConnectionRequest = 0xE0;
    private const byte CotpConnectionConfirm = 0xD0;
    private const byte ProtocolId = 0x32;
    private const byte RosctrJob = 0x01;
    private const byte RosctrAckData = 0x03;
    private const byte FunctionReadVar = 0x04;
    private const byte FunctionWriteVar = 0x05;
    private const byte FunctionSetupCommunication = 0xF0;
    private const byte ReturnSuccess = 0xFF;
    private const byte ReturnAddressOutOfRange = 0x05;
    private const byte TransportBit = 0x03;
    private const byte TransportByte = 0x04;

    public static byte[] EncodeConnectionRequest(S7Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var frame = new byte[22];
        WriteTpktHeader(frame, frame.Length);
        frame[4] = 0x11;
        frame[5] = CotpConnectionRequest;
        WriteUInt16BigEndian(frame.AsSpan(6, 2), 0x0000);
        WriteUInt16BigEndian(frame.AsSpan(8, 2), 0x0001);
        frame[10] = 0x00;
        frame[11] = 0xC0;
        frame[12] = 0x01;
        frame[13] = 0x0A;
        frame[14] = 0xC1;
        frame[15] = 0x02;
        WriteUInt16BigEndian(frame.AsSpan(16, 2), options.LocalTsap);
        frame[18] = 0xC2;
        frame[19] = 0x02;
        WriteUInt16BigEndian(frame.AsSpan(20, 2), options.EffectiveRemoteTsap);
        return frame;
    }

    public static byte[] EncodeConnectionConfirm(S7Options options)
    {
        var frame = EncodeConnectionRequest(options);
        frame[5] = CotpConnectionConfirm;
        WriteUInt16BigEndian(frame.AsSpan(6, 2), 0x0001);
        WriteUInt16BigEndian(frame.AsSpan(8, 2), 0x0000);
        return frame;
    }

    public static bool IsConnectionRequest(ReadOnlySpan<byte> frame)
        => IsTpkt(frame) && frame.Length >= 6 && frame[5] == CotpConnectionRequest;

    public static bool IsConnectionConfirm(ReadOnlySpan<byte> frame)
        => IsTpkt(frame) && frame.Length >= 6 && frame[5] == CotpConnectionConfirm;

    public static byte[] EncodeSetupCommunicationRequest(ushort pduReference, ushort requestedPduLength)
    {
        var parameters = new byte[8];
        parameters[0] = FunctionSetupCommunication;
        parameters[1] = 0x00;
        WriteUInt16BigEndian(parameters.AsSpan(2, 2), 1);
        WriteUInt16BigEndian(parameters.AsSpan(4, 2), 1);
        WriteUInt16BigEndian(parameters.AsSpan(6, 2), requestedPduLength);
        return EncodeS7Job(pduReference, parameters, []);
    }

    public static byte[] EncodeSetupCommunicationResponse(ushort pduReference, ushort pduLength)
    {
        var parameters = new byte[8];
        parameters[0] = FunctionSetupCommunication;
        parameters[1] = 0x00;
        WriteUInt16BigEndian(parameters.AsSpan(2, 2), 1);
        WriteUInt16BigEndian(parameters.AsSpan(4, 2), 1);
        WriteUInt16BigEndian(parameters.AsSpan(6, 2), pduLength);
        return EncodeS7AckData(pduReference, parameters, [], 0, 0);
    }

    public static bool TryDecodeSetupCommunicationResponse(ReadOnlySpan<byte> frame, ushort pduReference, out ushort pduLength)
    {
        pduLength = 0;
        if (!TryGetS7(frame, out var s7) || !TryReadHeader(s7, out var header))
        {
            return false;
        }

        if (header.Rosctr != RosctrAckData || header.PduReference != pduReference)
        {
            return false;
        }

        if (header.ErrorClass != 0 || header.ErrorCode != 0)
        {
            throw new ZeusProtocolException($"S7 建立通信失败：错误类 0x{header.ErrorClass:X2}，错误码 0x{header.ErrorCode:X2}。");
        }

        if (header.Parameters.Length < 8 || header.Parameters[0] != FunctionSetupCommunication)
        {
            throw new ZeusProtocolException("S7 建立通信响应参数异常。");
        }

        pduLength = ReadUInt16BigEndian(header.Parameters.Slice(6, 2));
        return true;
    }

    public static bool TryDecodeSetupCommunicationRequest(ReadOnlySpan<byte> frame, out ushort pduReference, out ushort requestedPduLength)
    {
        pduReference = 0;
        requestedPduLength = 0;
        if (!TryGetS7(frame, out var s7) || !TryReadHeader(s7, out var header))
        {
            return false;
        }

        if (header.Rosctr != RosctrJob || header.Parameters.Length < 8 || header.Parameters[0] != FunctionSetupCommunication)
        {
            return false;
        }

        pduReference = header.PduReference;
        requestedPduLength = ReadUInt16BigEndian(header.Parameters.Slice(6, 2));
        return true;
    }

    public static byte[] EncodeReadVarRequest(ushort pduReference, IReadOnlyList<S7VariableAddress> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is <= 0 or > byte.MaxValue)
        {
            throw new ZeusProtocolException($"S7 读取项数量必须在 1 到 255 之间，当前为 {items.Count}。");
        }

        var parameters = new byte[2 + (items.Count * 12)];
        parameters[0] = FunctionReadVar;
        parameters[1] = checked((byte)items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            WriteVariableAddress(parameters.AsSpan(2 + (i * 12), 12), items[i]);
        }

        return EncodeS7Job(pduReference, parameters, []);
    }

    public static byte[] EncodeWriteVarRequest(
        ushort pduReference,
        IReadOnlyList<S7VariableAddress> items,
        IReadOnlyList<byte[]> values)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(values);
        if (items.Count is <= 0 or > byte.MaxValue || items.Count != values.Count)
        {
            throw new ZeusProtocolException("S7 写入项与数据数量必须一致，且数量必须在 1 到 255 之间。");
        }

        var parameters = new byte[2 + (items.Count * 12)];
        parameters[0] = FunctionWriteVar;
        parameters[1] = checked((byte)items.Count);
        var dataLength = 0;
        for (var i = 0; i < items.Count; i++)
        {
            WriteVariableAddress(parameters.AsSpan(2 + (i * 12), 12), items[i]);
            dataLength += 4 + values[i].Length + PadLength(values[i].Length);
        }

        var data = new byte[dataLength];
        var offset = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var value = values[i];
            data[offset] = 0x00;
            data[offset + 1] = item.DataType == S7DataType.Bool ? TransportBit : TransportByte;
            WriteUInt16BigEndian(data.AsSpan(offset + 2, 2), checked((ushort)(item.DataType == S7DataType.Bool ? 1 : value.Length * 8)));
            value.CopyTo(data.AsSpan(offset + 4));
            offset += 4 + value.Length + PadLength(value.Length);
        }

        return EncodeS7Job(pduReference, parameters, data);
    }

    public static bool TryDecodeReadVarResponse(
        ReadOnlySpan<byte> frame,
        ushort pduReference,
        int itemCount,
        out byte[][] values)
    {
        values = [];
        if (!TryGetS7(frame, out var s7) || !TryReadHeader(s7, out var header))
        {
            return false;
        }

        if (header.Rosctr != RosctrAckData || header.PduReference != pduReference)
        {
            return false;
        }

        EnsureS7Success(header);
        if (header.Parameters.Length < 2 || header.Parameters[0] != FunctionReadVar || header.Parameters[1] != itemCount)
        {
            throw new ZeusProtocolException("S7 读取响应参数异常。请核对 PLC 返回帧。");
        }

        values = ReadResponseDataItems(header.Data, itemCount);
        return true;
    }

    public static bool TryDecodeWriteVarResponse(ReadOnlySpan<byte> frame, ushort pduReference, int itemCount)
    {
        if (!TryGetS7(frame, out var s7) || !TryReadHeader(s7, out var header))
        {
            return false;
        }

        if (header.Rosctr != RosctrAckData || header.PduReference != pduReference)
        {
            return false;
        }

        EnsureS7Success(header);
        if (header.Parameters.Length < 2 || header.Parameters[0] != FunctionWriteVar || header.Parameters[1] != itemCount)
        {
            throw new ZeusProtocolException("S7 写入响应参数异常。请核对 PLC 返回帧。");
        }

        if (header.Data.Length < itemCount)
        {
            throw new ZeusProtocolException("S7 写入响应数据长度不足。请核对 PLC 返回帧。");
        }

        for (var i = 0; i < itemCount; i++)
        {
            if (header.Data[i] != ReturnSuccess)
            {
                throw new ZeusProtocolException($"S7 写入第 {i + 1} 项失败，返回码 0x{header.Data[i]:X2}。");
            }
        }

        return true;
    }

    public static bool TryDecodeReadVarRequest(ReadOnlySpan<byte> frame, out ushort pduReference, out S7VariableAddress[] items)
    {
        pduReference = 0;
        items = [];
        if (!TryGetS7(frame, out var s7) || !TryReadHeader(s7, out var header))
        {
            return false;
        }

        if (header.Rosctr != RosctrJob || header.Parameters.Length < 2 || header.Parameters[0] != FunctionReadVar)
        {
            return false;
        }

        pduReference = header.PduReference;
        items = ReadVariableAddressList(header.Parameters);
        return true;
    }

    public static bool TryDecodeWriteVarRequest(
        ReadOnlySpan<byte> frame,
        out ushort pduReference,
        out S7VariableAddress[] items,
        out byte[][] values)
    {
        pduReference = 0;
        items = [];
        values = [];
        if (!TryGetS7(frame, out var s7) || !TryReadHeader(s7, out var header))
        {
            return false;
        }

        if (header.Rosctr != RosctrJob || header.Parameters.Length < 2 || header.Parameters[0] != FunctionWriteVar)
        {
            return false;
        }

        pduReference = header.PduReference;
        items = ReadVariableAddressList(header.Parameters);
        values = ReadRequestDataItems(header.Data, items);
        return true;
    }

    public static byte[] EncodeReadVarResponse(ushort pduReference, IReadOnlyList<byte[]?> values)
    {
        var parameters = new byte[] { FunctionReadVar, checked((byte)values.Count) };
        var dataLength = 0;
        foreach (var value in values)
        {
            var length = value?.Length ?? 0;
            dataLength += value is null ? 4 : 4 + length + PadLength(length);
        }

        var data = new byte[dataLength];
        var offset = 0;
        foreach (var value in values)
        {
            if (value is null)
            {
                data[offset] = ReturnAddressOutOfRange;
                data[offset + 1] = TransportByte;
                offset += 4;
                continue;
            }

            data[offset] = ReturnSuccess;
            data[offset + 1] = TransportByte;
            WriteUInt16BigEndian(data.AsSpan(offset + 2, 2), checked((ushort)(value.Length * 8)));
            value.CopyTo(data.AsSpan(offset + 4));
            offset += 4 + value.Length + PadLength(value.Length);
        }

        return EncodeS7AckData(pduReference, parameters, data, 0, 0);
    }

    public static byte[] EncodeWriteVarResponse(ushort pduReference, IReadOnlyList<bool> successes)
    {
        var parameters = new byte[] { FunctionWriteVar, checked((byte)successes.Count) };
        var data = new byte[successes.Count];
        for (var i = 0; i < successes.Count; i++)
        {
            data[i] = successes[i] ? ReturnSuccess : ReturnAddressOutOfRange;
        }

        return EncodeS7AckData(pduReference, parameters, data, 0, 0);
    }

    public static bool TryReadTpktFrame(IReadOnlyList<byte> buffer, out byte[] frame, out int consumed)
    {
        frame = [];
        consumed = 0;
        if (buffer.Count < 4)
        {
            return false;
        }

        if (buffer[0] != TpktVersion)
        {
            throw new ZeusProtocolException($"S7 TPKT 版本应为 0x03，实际为 0x{buffer[0]:X2}。");
        }

        var length = (buffer[2] << 8) | buffer[3];
        if (length < 7 || length > ProtocolReceiveBuffer.DefaultMaxBytes)
        {
            throw new ZeusProtocolException($"S7 TPKT 长度异常：{length}。");
        }

        if (buffer.Count < length)
        {
            return false;
        }

        frame = new byte[length];
        for (var i = 0; i < length; i++)
        {
            frame[i] = buffer[i];
        }

        consumed = length;
        return true;
    }

    public static S7VariableAddress CreateAddress(S7Area area, int dbNumber, int byteOffset, int bitOffset, S7DataType dataType)
    {
        return new S7VariableAddress(area, dbNumber, byteOffset, bitOffset, dataType, GetByteLength(dataType));
    }

    public static int GetByteLength(S7DataType dataType)
        => dataType switch
        {
            S7DataType.Bool or S7DataType.Byte => 1,
            S7DataType.Word or S7DataType.Int => 2,
            S7DataType.DWord or S7DataType.DInt or S7DataType.Real => 4,
            _ => throw new ZeusProtocolException($"不支持的 S7 数据类型：{dataType}。")
        };

    public static byte[] EncodeValue(S7DataType dataType, object value, double? scale = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        double? raw = scale is { } s ? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) / s : null;
        return dataType switch
        {
            S7DataType.Bool => [(byte)(Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture) ? 1 : 0)],
            S7DataType.Byte => [(byte)ConvertRounded(raw ?? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), byte.MinValue, byte.MaxValue, dataType)],
            S7DataType.Word => EncodeUInt16((ushort)ConvertRounded(raw ?? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), ushort.MinValue, ushort.MaxValue, dataType)),
            S7DataType.DWord => EncodeUInt32((uint)ConvertRounded(raw ?? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), uint.MinValue, uint.MaxValue, dataType)),
            S7DataType.Int => EncodeInt16((short)ConvertRounded(raw ?? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), short.MinValue, short.MaxValue, dataType)),
            S7DataType.DInt => EncodeInt32((int)ConvertRounded(raw ?? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), int.MinValue, int.MaxValue, dataType)),
            S7DataType.Real => EncodeSingle(scale is { } ? (float)raw!.Value : Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture)),
            _ => throw new ZeusProtocolException($"不支持的 S7 数据类型：{dataType}。")
        };
    }

    public static object DecodeValue(S7DataType dataType, ReadOnlySpan<byte> data, double? scale = null)
    {
        if (data.Length < GetByteLength(dataType))
        {
            throw new ZeusProtocolException($"S7 {dataType} 响应长度不足。");
        }

        object value = dataType switch
        {
            S7DataType.Bool => data[0] != 0,
            S7DataType.Byte => data[0],
            S7DataType.Word => ReadUInt16BigEndian(data),
            S7DataType.DWord => ReadUInt32BigEndian(data),
            S7DataType.Int => unchecked((short)ReadUInt16BigEndian(data)),
            S7DataType.DInt => unchecked((int)ReadUInt32BigEndian(data)),
            S7DataType.Real => BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32BigEndian(data))),
            _ => throw new ZeusProtocolException($"不支持的 S7 数据类型：{dataType}。")
        };

        return scale is null ? value : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) * scale.Value;
    }

    private static byte[] EncodeS7Job(ushort pduReference, ReadOnlySpan<byte> parameters, ReadOnlySpan<byte> data)
    {
        var s7 = new byte[10 + parameters.Length + data.Length];
        s7[0] = ProtocolId;
        s7[1] = RosctrJob;
        WriteUInt16BigEndian(s7.AsSpan(4, 2), pduReference);
        WriteUInt16BigEndian(s7.AsSpan(6, 2), checked((ushort)parameters.Length));
        WriteUInt16BigEndian(s7.AsSpan(8, 2), checked((ushort)data.Length));
        parameters.CopyTo(s7.AsSpan(10));
        data.CopyTo(s7.AsSpan(10 + parameters.Length));
        return EncodeCotpData(s7);
    }

    private static byte[] EncodeS7AckData(
        ushort pduReference,
        ReadOnlySpan<byte> parameters,
        ReadOnlySpan<byte> data,
        byte errorClass,
        byte errorCode)
    {
        var s7 = new byte[12 + parameters.Length + data.Length];
        s7[0] = ProtocolId;
        s7[1] = RosctrAckData;
        WriteUInt16BigEndian(s7.AsSpan(4, 2), pduReference);
        WriteUInt16BigEndian(s7.AsSpan(6, 2), checked((ushort)parameters.Length));
        WriteUInt16BigEndian(s7.AsSpan(8, 2), checked((ushort)data.Length));
        s7[10] = errorClass;
        s7[11] = errorCode;
        parameters.CopyTo(s7.AsSpan(12));
        data.CopyTo(s7.AsSpan(12 + parameters.Length));
        return EncodeCotpData(s7);
    }

    private static byte[] EncodeCotpData(ReadOnlySpan<byte> s7)
    {
        var frame = new byte[7 + s7.Length];
        WriteTpktHeader(frame, frame.Length);
        frame[4] = 0x02;
        frame[5] = CotpData;
        frame[6] = 0x80;
        s7.CopyTo(frame.AsSpan(7));
        return frame;
    }

    private static void WriteVariableAddress(Span<byte> destination, S7VariableAddress item)
    {
        destination[0] = 0x12;
        destination[1] = 0x0A;
        destination[2] = 0x10;
        destination[3] = item.DataType == S7DataType.Bool ? (byte)0x01 : (byte)0x02;
        WriteUInt16BigEndian(destination.Slice(4, 2), checked((ushort)(item.DataType == S7DataType.Bool ? 1 : item.ByteLength)));
        WriteUInt16BigEndian(destination.Slice(6, 2), checked((ushort)item.DbNumber));
        destination[8] = ToAreaCode(item.Area);
        var bitAddress = checked((item.ByteOffset * 8) + item.BitOffset);
        destination[9] = (byte)((bitAddress >> 16) & 0xFF);
        destination[10] = (byte)((bitAddress >> 8) & 0xFF);
        destination[11] = (byte)(bitAddress & 0xFF);
    }

    private static S7VariableAddress[] ReadVariableAddressList(ReadOnlySpan<byte> parameters)
    {
        if (parameters.Length < 2)
        {
            throw new ZeusProtocolException("S7 变量参数长度不足。");
        }

        var count = parameters[1];
        if (parameters.Length < 2 + (count * 12))
        {
            throw new ZeusProtocolException("S7 变量地址列表长度不足。");
        }

        var items = new S7VariableAddress[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = ReadVariableAddress(parameters.Slice(2 + (i * 12), 12));
        }

        return items;
    }

    private static S7VariableAddress ReadVariableAddress(ReadOnlySpan<byte> source)
    {
        if (source.Length < 12 || source[0] != 0x12 || source[1] != 0x0A || source[2] != 0x10)
        {
            throw new ZeusProtocolException("S7 变量地址格式异常。");
        }

        var dataType = source[3] == 0x01 ? S7DataType.Bool : S7DataType.Byte;
        var count = ReadUInt16BigEndian(source.Slice(4, 2));
        var dbNumber = ReadUInt16BigEndian(source.Slice(6, 2));
        var area = FromAreaCode(source[8]);
        var bitAddress = (source[9] << 16) | (source[10] << 8) | source[11];
        var byteOffset = bitAddress / 8;
        var bitOffset = bitAddress % 8;
        var byteLength = dataType == S7DataType.Bool ? 1 : count;
        return new S7VariableAddress(area, dbNumber, byteOffset, bitOffset, dataType, byteLength);
    }

    private static byte[][] ReadResponseDataItems(ReadOnlySpan<byte> data, int itemCount)
    {
        var values = new byte[itemCount][];
        var offset = 0;
        for (var i = 0; i < itemCount; i++)
        {
            if (data.Length < offset + 4)
            {
                throw new ZeusProtocolException("S7 读取响应数据项长度不足。");
            }

            var returnCode = data[offset];
            var transport = data[offset + 1];
            var bitLength = ReadUInt16BigEndian(data.Slice(offset + 2, 2));
            offset += 4;
            if (returnCode != ReturnSuccess)
            {
                throw new ZeusProtocolException($"S7 读取第 {i + 1} 项失败，返回码 0x{returnCode:X2}。");
            }

            var byteLength = transport == TransportBit ? 1 : (bitLength + 7) / 8;
            if (data.Length < offset + byteLength)
            {
                throw new ZeusProtocolException("S7 读取响应载荷长度不足。");
            }

            values[i] = data.Slice(offset, byteLength).ToArray();
            offset += byteLength + PadLength(byteLength);
        }

        return values;
    }

    private static byte[][] ReadRequestDataItems(ReadOnlySpan<byte> data, IReadOnlyList<S7VariableAddress> items)
    {
        var values = new byte[items.Count][];
        var offset = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (data.Length < offset + 4)
            {
                throw new ZeusProtocolException("S7 写入请求数据项长度不足。");
            }

            var transport = data[offset + 1];
            var bitLength = ReadUInt16BigEndian(data.Slice(offset + 2, 2));
            offset += 4;
            var byteLength = transport == TransportBit ? 1 : (bitLength + 7) / 8;
            if (data.Length < offset + byteLength)
            {
                throw new ZeusProtocolException("S7 写入请求载荷长度不足。");
            }

            values[i] = data.Slice(offset, byteLength).ToArray();
            offset += byteLength + PadLength(byteLength);
        }

        return values;
    }

    private static bool TryGetS7(ReadOnlySpan<byte> frame, out ReadOnlySpan<byte> s7)
    {
        s7 = default;
        if (!IsTpkt(frame) || frame.Length < 7 || frame[4] != 0x02 || frame[5] != CotpData)
        {
            return false;
        }

        s7 = frame[7..];
        return true;
    }

    private static bool TryReadHeader(ReadOnlySpan<byte> s7, out S7Header header)
    {
        header = default;
        if (s7.Length < 10 || s7[0] != ProtocolId)
        {
            return false;
        }

        var rosctr = s7[1];
        var pduReference = ReadUInt16BigEndian(s7.Slice(4, 2));
        var parameterLength = ReadUInt16BigEndian(s7.Slice(6, 2));
        var dataLength = ReadUInt16BigEndian(s7.Slice(8, 2));
        var offset = rosctr == RosctrAckData ? 12 : 10;
        if (s7.Length < offset + parameterLength + dataLength)
        {
            throw new ZeusProtocolException("S7 帧长度与头部声明不一致。");
        }

        header = new S7Header(
            rosctr,
            pduReference,
            s7.Slice(offset, parameterLength),
            s7.Slice(offset + parameterLength, dataLength),
            rosctr == RosctrAckData ? s7[10] : (byte)0,
            rosctr == RosctrAckData ? s7[11] : (byte)0);
        return true;
    }

    private static void EnsureS7Success(S7Header header)
    {
        if (header.ErrorClass != 0 || header.ErrorCode != 0)
        {
            throw new ZeusProtocolException($"S7 响应错误：错误类 0x{header.ErrorClass:X2}，错误码 0x{header.ErrorCode:X2}。");
        }
    }

    private static bool IsTpkt(ReadOnlySpan<byte> frame)
        => frame.Length >= 4 && frame[0] == TpktVersion && (((frame[2] << 8) | frame[3]) <= frame.Length);

    private static void WriteTpktHeader(Span<byte> frame, int length)
    {
        frame[0] = TpktVersion;
        frame[1] = 0x00;
        WriteUInt16BigEndian(frame.Slice(2, 2), checked((ushort)length));
    }

    private static byte ToAreaCode(S7Area area)
        => area switch
        {
            S7Area.Inputs => 0x81,
            S7Area.Outputs => 0x82,
            S7Area.Merkers => 0x83,
            S7Area.DataBlock => 0x84,
            _ => throw new ZeusProtocolException($"不支持的 S7 存储区：{area}。")
        };

    private static S7Area FromAreaCode(byte area)
        => area switch
        {
            0x81 => S7Area.Inputs,
            0x82 => S7Area.Outputs,
            0x83 => S7Area.Merkers,
            0x84 => S7Area.DataBlock,
            _ => throw new ZeusProtocolException($"不支持的 S7 存储区代码：0x{area:X2}。")
        };

    private static int PadLength(int byteLength) => byteLength % 2 == 0 ? 0 : 1;

    public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> data)
        => (ushort)((data[0] << 8) | data[1]);

    public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> data)
        => (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);

    public static void WriteUInt16BigEndian(Span<byte> dest, ushort value)
    {
        dest[0] = (byte)(value >> 8);
        dest[1] = (byte)(value & 0xFF);
    }

    public static void WriteUInt32BigEndian(Span<byte> dest, uint value)
    {
        dest[0] = (byte)(value >> 24);
        dest[1] = (byte)((value >> 16) & 0xFF);
        dest[2] = (byte)((value >> 8) & 0xFF);
        dest[3] = (byte)(value & 0xFF);
    }

    private static byte[] EncodeUInt16(ushort value)
    {
        var data = new byte[2];
        WriteUInt16BigEndian(data, value);
        return data;
    }

    private static byte[] EncodeInt16(short value) => EncodeUInt16(unchecked((ushort)value));

    private static byte[] EncodeUInt32(uint value)
    {
        var data = new byte[4];
        WriteUInt32BigEndian(data, value);
        return data;
    }

    private static byte[] EncodeInt32(int value) => EncodeUInt32(unchecked((uint)value));

    private static byte[] EncodeSingle(float value) => EncodeUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static double ConvertRounded(double value, double min, double max, S7DataType dataType)
    {
        if (!double.IsFinite(value))
        {
            throw new ZeusProtocolException($"S7 {dataType} 写入值必须是有限数值。");
        }

        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded < min || rounded > max)
        {
            throw new ZeusProtocolException($"S7 {dataType} 写入值 {value} 超出范围 {min} 到 {max}。");
        }

        return rounded;
    }
}

internal readonly record struct S7VariableAddress(
    S7Area Area,
    int DbNumber,
    int ByteOffset,
    int BitOffset,
    S7DataType DataType,
    int ByteLength);

internal readonly ref struct S7Header
{
    public S7Header(
        byte rosctr,
        ushort pduReference,
        ReadOnlySpan<byte> parameters,
        ReadOnlySpan<byte> data,
        byte errorClass,
        byte errorCode)
    {
        Rosctr = rosctr;
        PduReference = pduReference;
        Parameters = parameters;
        Data = data;
        ErrorClass = errorClass;
        ErrorCode = errorCode;
    }

    public byte Rosctr { get; }

    public ushort PduReference { get; }

    public ReadOnlySpan<byte> Parameters { get; }

    public ReadOnlySpan<byte> Data { get; }

    public byte ErrorClass { get; }

    public byte ErrorCode { get; }
}
