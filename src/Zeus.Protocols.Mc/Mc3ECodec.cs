namespace Zeus;

/// <summary>
/// Mitsubishi MC Protocol 帧编解码。
/// </summary>
internal static class Mc3ECodec
{
    public const ushort BatchReadCommand = 0x0401;
    public const ushort BatchWriteCommand = 0x1401;
    public const ushort RandomReadCommand = 0x0403;
    public const ushort RandomWriteCommand = 0x1402;
    public const ushort MultipleBlockReadCommand = 0x0406;
    public const ushort RemoteRunCommand = 0x1001;
    public const ushort RemoteStopCommand = 0x1002;
    public const ushort RemotePauseCommand = 0x1003;
    public const ushort RemoteLatchClearCommand = 0x1005;
    public const ushort RemoteResetCommand = 0x1006;
    public const ushort WordSubcommand = 0x0000;
    public const ushort BitSubcommand = 0x0001;

    private const byte OneEReadBits = 0x00;
    private const byte OneEReadWords = 0x01;
    private const byte OneEWriteBits = 0x02;
    private const byte OneEWriteWords = 0x03;

    public static byte[] EncodeRequest(Mc3EOptions options, ushort command, ushort subcommand, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.FrameType == McFrameType.Frame1E)
        {
            throw new ZeusProtocolException("MC 1E 帧不支持原始 command/subcommand 请求。请使用批量读写软元件 API。");
        }

        return options.DataEncoding == McDataEncoding.Ascii
            ? EncodeAsciiRequest(options, command, subcommand, data)
            : EncodeBinaryRequest(options, command, subcommand, data);
    }

    public static byte[] EncodeDeviceRequest(Mc3EOptions options, McOperation operation, ReadOnlySpan<byte> canonicalData)
    {
        ArgumentNullException.ThrowIfNull(options);
        var (command, subcommand) = ToCommand(operation);
        return options.FrameType == McFrameType.Frame1E
            ? Encode1ERequest(options, operation, canonicalData)
            : EncodeRequest(options, command, subcommand, EncodeDeviceData(options, operation, canonicalData));
    }

    public static bool TryDecodeRawResponse(
        IReadOnlyList<byte> buffer,
        Mc3EOptions options,
        out ushort endCode,
        out byte[] data,
        out int consumed)
    {
        endCode = 0;
        data = [];
        consumed = 0;
        if (options.FrameType == McFrameType.Frame1E)
        {
            throw new ZeusProtocolException("MC 1E 帧响应没有长度字段，不能用于原始 ExecuteAsync。请使用批量读写软元件 API。");
        }

        return options.DataEncoding == McDataEncoding.Ascii
            ? TryDecodeAsciiResponse(buffer, options.FrameType, out endCode, out data, out consumed)
            : TryDecodeBinaryResponse(buffer, options.FrameType, out endCode, out data, out consumed);
    }

    public static bool TryDecodeDeviceResponse(
        IReadOnlyList<byte> buffer,
        Mc3EOptions options,
        McPendingRequest pending,
        out ushort endCode,
        out byte[] data,
        out int consumed)
    {
        endCode = 0;
        data = [];
        consumed = 0;

        if (options.FrameType == McFrameType.Frame1E)
        {
            return options.DataEncoding == McDataEncoding.Ascii
                ? TryDecode1EAsciiResponse(buffer, pending, out endCode, out data, out consumed)
                : TryDecode1EBinaryResponse(buffer, pending, out endCode, out data, out consumed);
        }

        var decoded = options.DataEncoding == McDataEncoding.Ascii
            ? TryDecodeAsciiResponse(buffer, options.FrameType, out endCode, out var raw, out consumed)
            : TryDecodeBinaryResponse(buffer, options.FrameType, out endCode, out raw, out consumed);
        if (!decoded)
        {
            return false;
        }

        data = endCode == 0 && IsReadOperation(pending.Operation)
            ? DecodeDeviceResponseData(options.DataEncoding, pending.Operation, pending.Points, pending.ExtraPoints, raw, false)
            : [];
        return true;
    }

    public static bool TryDecodeRequest(
        ReadOnlySpan<byte> frame,
        out McRequestContext context,
        out ushort command,
        out ushort subcommand,
        out byte[] data)
    {
        context = default;
        command = 0;
        subcommand = 0;
        data = [];

        try
        {
            if (TryDecodeBinaryRequest(frame, out context, out command, out subcommand, out data)
                || TryDecodeAsciiRequest(frame, out context, out command, out subcommand, out data)
                || TryDecode1ERequest(frame, out context, out command, out subcommand, out data))
            {
                return true;
            }
        }
        catch (ZeusProtocolException)
        {
            return false;
        }

        return false;
    }

    public static byte[] EncodeResponse(McRequestContext context, ushort endCode, ReadOnlySpan<byte> data)
        => context.FrameType switch
        {
            McFrameType.Frame1E => Encode1EResponse(context, endCode, data),
            _ => context.DataEncoding == McDataEncoding.Ascii
                ? EncodeAsciiResponse(context, endCode, data)
                : EncodeBinaryResponse(context, endCode, data)
        };

    public static byte[] BuildDeviceRequest(int address, McDeviceCode deviceCode, ushort points)
    {
        ValidateAddress(address);
        var data = new byte[6];
        WriteDeviceAddress(data, new McDeviceAddress(deviceCode, address));
        WriteUInt16LittleEndian(data.AsSpan(4, 2), points);
        return data;
    }

    public static byte[] BuildRandomReadRequest(
        IReadOnlyList<McDeviceAddress> wordDevices,
        IReadOnlyList<McDeviceAddress> doubleWordDevices)
    {
        ArgumentNullException.ThrowIfNull(wordDevices);
        ArgumentNullException.ThrowIfNull(doubleWordDevices);
        var data = new byte[2 + ((wordDevices.Count + doubleWordDevices.Count) * 4)];
        data[0] = checked((byte)wordDevices.Count);
        data[1] = checked((byte)doubleWordDevices.Count);
        var offset = 2;
        foreach (var device in wordDevices)
        {
            WriteDeviceAddress(data.AsSpan(offset, 4), device);
            offset += 4;
        }

        foreach (var device in doubleWordDevices)
        {
            WriteDeviceAddress(data.AsSpan(offset, 4), device);
            offset += 4;
        }

        return data;
    }

    public static byte[] BuildRandomWriteWordsRequest(
        IReadOnlyList<McWordWrite> wordValues,
        IReadOnlyList<McDoubleWordWrite> doubleWordValues)
    {
        ArgumentNullException.ThrowIfNull(wordValues);
        ArgumentNullException.ThrowIfNull(doubleWordValues);
        var data = new byte[2 + (wordValues.Count * 6) + (doubleWordValues.Count * 8)];
        data[0] = checked((byte)wordValues.Count);
        data[1] = checked((byte)doubleWordValues.Count);
        var offset = 2;
        foreach (var item in wordValues)
        {
            WriteDeviceAddress(data.AsSpan(offset, 4), new McDeviceAddress(item.DeviceCode, item.Address));
            WriteUInt16LittleEndian(data.AsSpan(offset + 4, 2), item.Value);
            offset += 6;
        }

        foreach (var item in doubleWordValues)
        {
            WriteDeviceAddress(data.AsSpan(offset, 4), new McDeviceAddress(item.DeviceCode, item.Address));
            WriteUInt32LittleEndian(data.AsSpan(offset + 4, 4), item.Value);
            offset += 8;
        }

        return data;
    }

    public static byte[] BuildRandomWriteBitsRequest(IReadOnlyList<McBitWrite> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var data = new byte[1 + (values.Count * 5)];
        data[0] = checked((byte)values.Count);
        var offset = 1;
        foreach (var item in values)
        {
            WriteDeviceAddress(data.AsSpan(offset, 4), new McDeviceAddress(item.DeviceCode, item.Address));
            data[offset + 4] = item.Value ? (byte)1 : (byte)0;
            offset += 5;
        }

        return data;
    }

    /// <summary>
    /// 构造 3E/4E 多块批量读取请求。字块与位块各自连续，一次事务返回拼接后的数据。
    /// </summary>
    public static byte[] BuildMultipleBlockReadRequest(
        IReadOnlyList<McDeviceRange> wordBlocks,
        IReadOnlyList<McDeviceRange>? bitBlocks = null)
    {
        ArgumentNullException.ThrowIfNull(wordBlocks);
        bitBlocks ??= Array.Empty<McDeviceRange>();
        if (wordBlocks.Count == 0 && bitBlocks.Count == 0)
        {
            throw new ZeusProtocolException("MC 多块批量读取至少需要 1 个字块或位块。");
        }

        if (wordBlocks.Count > byte.MaxValue || bitBlocks.Count > byte.MaxValue)
        {
            throw new ZeusProtocolException("MC 多块批量读取的字块和位块数量都不能超过 255。");
        }

        var data = new byte[2 + ((wordBlocks.Count + bitBlocks.Count) * 6)];
        data[0] = (byte)wordBlocks.Count;
        data[1] = (byte)bitBlocks.Count;
        var offset = 2;
        foreach (var block in wordBlocks.Concat(bitBlocks))
        {
            if (block.Points == 0)
            {
                throw new ZeusProtocolException("MC 多块批量读取的每个块点数必须大于 0。");
            }

            WriteDeviceAddress(data.AsSpan(offset, 4), new McDeviceAddress(block.DeviceCode, block.Address));
            WriteUInt16LittleEndian(data.AsSpan(offset + 4, 2), block.Points);
            offset += 6;
        }

        return data;
    }

    public static (McDeviceRange[] WordBlocks, McDeviceRange[] BitBlocks) ReadMultipleBlockReadRequest(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            throw new ZeusProtocolException("MC 多块批量读取请求长度不足。");
        }

        var wordCount = data[0];
        var bitCount = data[1];
        var needed = 2 + ((wordCount + bitCount) * 6);
        if (data.Length < needed)
        {
            throw new ZeusProtocolException("MC 多块批量读取软元件列表长度不足。");
        }

        var wordBlocks = new McDeviceRange[wordCount];
        var bitBlocks = new McDeviceRange[bitCount];
        var offset = 2;
        for (var i = 0; i < wordCount; i++)
        {
            var (address, deviceCode, points) = ReadDeviceRequest(data.Slice(offset, 6));
            wordBlocks[i] = new McDeviceRange(deviceCode, address, points);
            offset += 6;
        }

        for (var i = 0; i < bitCount; i++)
        {
            var (address, deviceCode, points) = ReadDeviceRequest(data.Slice(offset, 6));
            bitBlocks[i] = new McDeviceRange(deviceCode, address, points);
            offset += 6;
        }

        return (wordBlocks, bitBlocks);
    }

    /// <summary>
    /// 把二进制载荷编码进 ASCII 帧数据区（每字节两个十六进制字符）。
    /// </summary>
    public static byte[] EncodeRawPayload(Mc3EOptions options, ReadOnlySpan<byte> binary)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.DataEncoding != McDataEncoding.Ascii)
        {
            return binary.ToArray();
        }

        var ascii = new byte[binary.Length * 2];
        for (var i = 0; i < binary.Length; i++)
        {
            WriteAsciiHex(ascii, i * 2, binary[i], 2);
        }

        return ascii;
    }

    public static (McDeviceAddress[] WordDevices, McDeviceAddress[] DoubleWordDevices) ReadRandomReadRequest(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            throw new ZeusProtocolException("MC 随机读取请求长度不足。");
        }

        var wordCount = data[0];
        var doubleWordCount = data[1];
        var expected = 2 + ((wordCount + doubleWordCount) * 4);
        if (data.Length < expected)
        {
            throw new ZeusProtocolException("MC 随机读取软元件列表长度不足。");
        }

        var wordDevices = new McDeviceAddress[wordCount];
        var doubleWordDevices = new McDeviceAddress[doubleWordCount];
        var offset = 2;
        for (var i = 0; i < wordDevices.Length; i++)
        {
            wordDevices[i] = ReadDeviceAddress(data.Slice(offset, 4));
            offset += 4;
        }

        for (var i = 0; i < doubleWordDevices.Length; i++)
        {
            doubleWordDevices[i] = ReadDeviceAddress(data.Slice(offset, 4));
            offset += 4;
        }

        return (wordDevices, doubleWordDevices);
    }

    public static (McWordWrite[] WordValues, McDoubleWordWrite[] DoubleWordValues) ReadRandomWriteWordsRequest(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            throw new ZeusProtocolException("MC 随机写入字软元件请求长度不足。");
        }

        var wordCount = data[0];
        var doubleWordCount = data[1];
        var expected = 2 + (wordCount * 6) + (doubleWordCount * 8);
        if (data.Length < expected)
        {
            throw new ZeusProtocolException("MC 随机写入字软元件数据长度不足。");
        }

        var wordValues = new McWordWrite[wordCount];
        var doubleWordValues = new McDoubleWordWrite[doubleWordCount];
        var offset = 2;
        for (var i = 0; i < wordValues.Length; i++)
        {
            var device = ReadDeviceAddress(data.Slice(offset, 4));
            wordValues[i] = new McWordWrite(device.DeviceCode, device.Address, ReadUInt16LittleEndian(data.Slice(offset + 4, 2)));
            offset += 6;
        }

        for (var i = 0; i < doubleWordValues.Length; i++)
        {
            var device = ReadDeviceAddress(data.Slice(offset, 4));
            doubleWordValues[i] = new McDoubleWordWrite(device.DeviceCode, device.Address, ReadUInt32LittleEndian(data.Slice(offset + 4, 4)));
            offset += 8;
        }

        return (wordValues, doubleWordValues);
    }

    public static McBitWrite[] ReadRandomWriteBitsRequest(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            throw new ZeusProtocolException("MC 随机写入位软元件请求长度不足。");
        }

        var count = data[0];
        var expected = 1 + (count * 5);
        if (data.Length < expected)
        {
            throw new ZeusProtocolException("MC 随机写入位软元件数据长度不足。");
        }

        var values = new McBitWrite[count];
        var offset = 1;
        for (var i = 0; i < values.Length; i++)
        {
            var device = ReadDeviceAddress(data.Slice(offset, 4));
            values[i] = new McBitWrite(device.DeviceCode, device.Address, data[offset + 4] != 0);
            offset += 5;
        }

        return values;
    }

    public static McRandomReadResult ReadRandomReadResponse(ReadOnlySpan<byte> data, ushort wordCount, ushort doubleWordCount)
    {
        var expected = (wordCount * 2) + (doubleWordCount * 4);
        if (data.Length < expected)
        {
            throw new ZeusProtocolException("MC 随机读取响应长度不足。请核对 PLC 返回数据。");
        }

        var wordValues = new ushort[wordCount];
        var doubleWordValues = new uint[doubleWordCount];
        var offset = 0;
        for (var i = 0; i < wordValues.Length; i++)
        {
            wordValues[i] = ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;
        }

        for (var i = 0; i < doubleWordValues.Length; i++)
        {
            doubleWordValues[i] = ReadUInt32LittleEndian(data.Slice(offset, 4));
            offset += 4;
        }

        return new McRandomReadResult(wordValues, doubleWordValues);
    }

    public static (int Address, McDeviceCode DeviceCode, ushort Points) ReadDeviceRequest(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
        {
            throw new ZeusProtocolException("MC 软元件请求长度不足。");
        }

        var address = data[0] | (data[1] << 8) | (data[2] << 16);
        var deviceCode = (McDeviceCode)data[3];
        var points = ReadUInt16LittleEndian(data.Slice(4, 2));
        return (address, deviceCode, points);
    }

    private static McDeviceAddress ReadDeviceAddress(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            throw new ZeusProtocolException("MC 软元件地址长度不足。");
        }

        var address = data[0] | (data[1] << 8) | (data[2] << 16);
        return new McDeviceAddress((McDeviceCode)data[3], address);
    }

    private static void WriteDeviceAddress(Span<byte> destination, McDeviceAddress device)
    {
        ValidateAddress(device.Address);
        destination[0] = (byte)(device.Address & 0xFF);
        destination[1] = (byte)((device.Address >> 8) & 0xFF);
        destination[2] = (byte)((device.Address >> 16) & 0xFF);
        destination[3] = (byte)device.DeviceCode;
    }

    public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> data)
        => (ushort)(data[0] | (data[1] << 8));

    public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> data)
        => (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));

    public static void WriteUInt16LittleEndian(Span<byte> dest, ushort value)
    {
        dest[0] = (byte)(value & 0xFF);
        dest[1] = (byte)(value >> 8);
    }

    public static void WriteUInt32LittleEndian(Span<byte> dest, uint value)
    {
        dest[0] = (byte)(value & 0xFF);
        dest[1] = (byte)((value >> 8) & 0xFF);
        dest[2] = (byte)((value >> 16) & 0xFF);
        dest[3] = (byte)(value >> 24);
    }

    public static int BitByteCount(int points) => (points + 1) / 2;

    public static bool GetPackedBit(ReadOnlySpan<byte> data, int index)
    {
        var value = data[index / 2];
        return (index % 2 == 0 ? value >> 4 : value & 0x0F) != 0;
    }

    public static void SetPackedBit(Span<byte> data, int index, bool value)
    {
        if (!value)
        {
            return;
        }

        var offset = index / 2;
        data[offset] |= (byte)(index % 2 == 0 ? 0x10 : 0x01);
    }

    public static McPendingRequest CreatePending(McOperation operation, ushort points, ushort extraPoints = 0)
        => new(operation, points, extraPoints);

    public static McOperation ToOperation(ushort command, ushort subcommand)
        => command switch
        {
            BatchReadCommand when subcommand == WordSubcommand => McOperation.ReadWords,
            BatchWriteCommand when subcommand == WordSubcommand => McOperation.WriteWords,
            BatchReadCommand when subcommand == BitSubcommand => McOperation.ReadBits,
            BatchWriteCommand when subcommand == BitSubcommand => McOperation.WriteBits,
            RandomReadCommand when subcommand == WordSubcommand => McOperation.RandomRead,
            RandomWriteCommand when subcommand == WordSubcommand => McOperation.RandomWriteWords,
            RandomWriteCommand when subcommand == BitSubcommand => McOperation.RandomWriteBits,
            _ => McOperation.Unknown
        };

    private static byte[] EncodeBinaryRequest(Mc3EOptions options, ushort command, ushort subcommand, ReadOnlySpan<byte> data)
    {
        var payloadLength = 2 + 2 + data.Length;
        var requestLength = 2 + payloadLength;
        if (requestLength > ushort.MaxValue)
        {
            throw new ZeusProtocolException("MC 请求过长，无法放入 Binary 帧。");
        }

        var offset = options.FrameType == McFrameType.Frame4E ? 6 : 2;
        var frame = new byte[offset + 13 + data.Length];
        if (options.FrameType == McFrameType.Frame4E)
        {
            frame[0] = 0x54;
            frame[1] = 0x00;
            WriteUInt16LittleEndian(frame.AsSpan(2, 2), options.SerialNumber);
            frame[4] = 0x00;
            frame[5] = 0x00;
        }
        else
        {
            frame[0] = 0x50;
            frame[1] = 0x00;
        }

        WriteBinaryRoute(frame.AsSpan(offset), options);
        WriteUInt16LittleEndian(frame.AsSpan(offset + 5, 2), (ushort)requestLength);
        WriteUInt16LittleEndian(frame.AsSpan(offset + 7, 2), options.MonitoringTimer);
        WriteUInt16LittleEndian(frame.AsSpan(offset + 9, 2), command);
        WriteUInt16LittleEndian(frame.AsSpan(offset + 11, 2), subcommand);
        data.CopyTo(frame.AsSpan(offset + 13));
        return frame;
    }

    private static byte[] EncodeAsciiRequest(Mc3EOptions options, ushort command, ushort subcommand, ReadOnlySpan<byte> data)
    {
        var requestLength = 12 + data.Length;
        if (requestLength > ushort.MaxValue)
        {
            throw new ZeusProtocolException("MC 请求过长，无法放入 ASCII 帧。");
        }

        var offset = options.FrameType == McFrameType.Frame4E ? 12 : 4;
        var frame = new byte[offset + 26 + data.Length];
        WriteAsciiText(frame, 0, options.FrameType == McFrameType.Frame4E ? "5400" : "5000");
        if (options.FrameType == McFrameType.Frame4E)
        {
            WriteAsciiHex(frame, 4, options.SerialNumber, 4);
            WriteAsciiText(frame, 8, "0000");
        }

        WriteAsciiRoute(frame.AsSpan(offset), options);
        WriteAsciiHex(frame, offset + 10, requestLength, 4);
        WriteAsciiHex(frame, offset + 14, options.MonitoringTimer, 4);
        WriteAsciiHex(frame, offset + 18, command, 4);
        WriteAsciiHex(frame, offset + 22, subcommand, 4);
        data.CopyTo(frame.AsSpan(offset + 26));
        return frame;
    }

    private static byte[] EncodeBinaryResponse(McRequestContext context, ushort endCode, ReadOnlySpan<byte> data)
    {
        var responseData = endCode == 0 ? EncodeDeviceResponseData(context, data) : [];
        var responseLength = 2 + responseData.Length;
        if (responseLength > ushort.MaxValue)
        {
            throw new ZeusProtocolException("MC 响应过长，无法放入 Binary 帧。");
        }

        var offset = context.FrameType == McFrameType.Frame4E ? 6 : 2;
        var frame = new byte[offset + 9 + responseData.Length];
        if (context.FrameType == McFrameType.Frame4E)
        {
            frame[0] = 0xD4;
            frame[1] = 0x00;
            WriteUInt16LittleEndian(frame.AsSpan(2, 2), context.SerialNumber);
            frame[4] = 0x00;
            frame[5] = 0x00;
        }
        else
        {
            frame[0] = 0xD0;
            frame[1] = 0x00;
        }

        WriteBinaryRoute(frame.AsSpan(offset), context);
        WriteUInt16LittleEndian(frame.AsSpan(offset + 5, 2), (ushort)responseLength);
        WriteUInt16LittleEndian(frame.AsSpan(offset + 7, 2), endCode);
        responseData.CopyTo(frame.AsSpan(offset + 9));
        return frame;
    }

    private static byte[] EncodeAsciiResponse(McRequestContext context, ushort endCode, ReadOnlySpan<byte> data)
    {
        var responseData = endCode == 0 ? EncodeDeviceResponseData(context, data) : [];
        var responseLength = 4 + responseData.Length;
        if (responseLength > ushort.MaxValue)
        {
            throw new ZeusProtocolException("MC 响应过长，无法放入 ASCII 帧。");
        }

        var offset = context.FrameType == McFrameType.Frame4E ? 12 : 4;
        var frame = new byte[offset + 18 + responseData.Length];
        WriteAsciiText(frame, 0, context.FrameType == McFrameType.Frame4E ? "D400" : "D000");
        if (context.FrameType == McFrameType.Frame4E)
        {
            WriteAsciiHex(frame, 4, context.SerialNumber, 4);
            WriteAsciiText(frame, 8, "0000");
        }

        WriteAsciiRoute(frame.AsSpan(offset), context);
        WriteAsciiHex(frame, offset + 10, responseLength, 4);
        WriteAsciiHex(frame, offset + 14, endCode, 4);
        responseData.CopyTo(frame.AsSpan(offset + 18));
        return frame;
    }

    private static bool TryDecodeBinaryRequest(
        ReadOnlySpan<byte> frame,
        out McRequestContext context,
        out ushort command,
        out ushort subcommand,
        out byte[] data)
    {
        context = default;
        command = 0;
        subcommand = 0;
        data = [];
        if (frame.Length < 15)
        {
            return false;
        }

        McFrameType frameType;
        ushort serial = 0;
        int offset;
        if (frame[0] == 0x50 && frame[1] == 0x00)
        {
            frameType = McFrameType.Frame3E;
            offset = 2;
        }
        else if (frame.Length >= 19 && frame[0] == 0x54 && frame[1] == 0x00)
        {
            frameType = McFrameType.Frame4E;
            serial = ReadUInt16LittleEndian(frame.Slice(2, 2));
            offset = 6;
        }
        else
        {
            return false;
        }

        var requestLength = ReadUInt16LittleEndian(frame.Slice(offset + 5, 2));
        if (requestLength < 6 || frame.Length < offset + 7 + requestLength)
        {
            return false;
        }

        command = ReadUInt16LittleEndian(frame.Slice(offset + 9, 2));
        subcommand = ReadUInt16LittleEndian(frame.Slice(offset + 11, 2));
        data = frame.Slice(offset + 13, requestLength - 6).ToArray();
        var operation = ToOperation(command, subcommand);
        var (points, extraPoints) = TryReadPointCounts(operation, data);
        context = new McRequestContext(
            frameType,
            McDataEncoding.Binary,
            frame[offset],
            frame[offset + 1],
            ReadUInt16LittleEndian(frame.Slice(offset + 2, 2)),
            frame[offset + 4],
            serial,
            operation,
            points,
            extraPoints,
            command,
            subcommand,
            0);
        return true;
    }

    private static bool TryDecodeAsciiRequest(
        ReadOnlySpan<byte> frame,
        out McRequestContext context,
        out ushort command,
        out ushort subcommand,
        out byte[] data)
    {
        context = default;
        command = 0;
        subcommand = 0;
        data = [];

        McFrameType frameType;
        ushort serial = 0;
        int offset;
        if (StartsWithAscii(frame, "5000"))
        {
            frameType = McFrameType.Frame3E;
            offset = 4;
        }
        else if (StartsWithAscii(frame, "5400"))
        {
            frameType = McFrameType.Frame4E;
            if (frame.Length < 38)
            {
                return false;
            }

            serial = (ushort)ReadAsciiHex(frame, 4, 4);
            offset = 12;
        }
        else
        {
            return false;
        }

        if (frame.Length < offset + 30)
        {
            return false;
        }

        var requestLength = ReadAsciiHex(frame, offset + 10, 4);
        if (requestLength < 12 || frame.Length < offset + 14 + requestLength)
        {
            return false;
        }

        command = (ushort)ReadAsciiHex(frame, offset + 18, 4);
        subcommand = (ushort)ReadAsciiHex(frame, offset + 22, 4);
        var operation = ToOperation(command, subcommand);
        var asciiData = frame.Slice(offset + 26, requestLength - 12);
        data = Decode3EAsciiDeviceData(operation, asciiData);
        var (points, extraPoints) = TryReadPointCounts(operation, data);
        context = new McRequestContext(
            frameType,
            McDataEncoding.Ascii,
            (byte)ReadAsciiHex(frame, offset, 2),
            (byte)ReadAsciiHex(frame, offset + 2, 2),
            (ushort)ReadAsciiHex(frame, offset + 4, 4),
            (byte)ReadAsciiHex(frame, offset + 8, 2),
            serial,
            operation,
            points,
            extraPoints,
            command,
            subcommand,
            0);
        return true;
    }

    private static bool TryDecode1ERequest(
        ReadOnlySpan<byte> frame,
        out McRequestContext context,
        out ushort command,
        out ushort subcommand,
        out byte[] data)
    {
        context = default;
        command = 0;
        subcommand = 0;
        data = [];

        if (frame.Length >= 12 && Is1ECommand(frame[0]))
        {
            var oneECommand = frame[0];
            var operation = To1EOperation(oneECommand);
            (command, subcommand) = ToCommand(operation);
            data = Decode1EBinaryDeviceData(operation, frame.Slice(4));
            var (points, extraPoints) = TryReadPointCounts(operation, data);
            context = new McRequestContext(
                McFrameType.Frame1E,
                McDataEncoding.Binary,
                0,
                frame[1],
                0,
                0,
                0,
                operation,
                points,
                extraPoints,
                command,
                subcommand,
                oneECommand);
            return true;
        }

        if (frame.Length >= 24 && IsAsciiHex(frame.Slice(0, 8)))
        {
            var oneECommand = (byte)ReadAsciiHex(frame, 0, 2);
            if (!Is1ECommand(oneECommand))
            {
                return false;
            }

            var operation = To1EOperation(oneECommand);
            (command, subcommand) = ToCommand(operation);
            data = Decode1EAsciiDeviceData(operation, frame.Slice(8));
            var (points, extraPoints) = TryReadPointCounts(operation, data);
            context = new McRequestContext(
                McFrameType.Frame1E,
                McDataEncoding.Ascii,
                0,
                (byte)ReadAsciiHex(frame, 2, 2),
                0,
                0,
                0,
                operation,
                points,
                extraPoints,
                command,
                subcommand,
                oneECommand);
            return true;
        }

        return false;
    }

    private static bool TryDecodeBinaryResponse(
        IReadOnlyList<byte> buffer,
        McFrameType frameType,
        out ushort endCode,
        out byte[] data,
        out int consumed)
    {
        endCode = 0;
        data = [];
        consumed = 0;
        var offset = frameType == McFrameType.Frame4E ? 6 : 2;
        var min = offset + 9;
        if (buffer.Count < min)
        {
            return false;
        }

        var expected0 = frameType == McFrameType.Frame4E ? (byte)0xD4 : (byte)0xD0;
        if (buffer[0] != expected0 || buffer[1] != 0x00)
        {
            throw new ZeusProtocolException($"MC {frameType} Binary 响应头错误。");
        }

        var responseLength = (ushort)(buffer[offset + 5] | (buffer[offset + 6] << 8));
        if (responseLength < 2)
        {
            throw new ZeusProtocolException("MC Binary 响应长度字段非法。");
        }

        var total = offset + 7 + responseLength;
        if (buffer.Count < total)
        {
            return false;
        }

        endCode = (ushort)(buffer[offset + 7] | (buffer[offset + 8] << 8));
        data = Copy(buffer, offset + 9, responseLength - 2);
        consumed = total;
        return true;
    }

    private static bool TryDecodeAsciiResponse(
        IReadOnlyList<byte> buffer,
        McFrameType frameType,
        out ushort endCode,
        out byte[] data,
        out int consumed)
    {
        endCode = 0;
        data = [];
        consumed = 0;
        var offset = frameType == McFrameType.Frame4E ? 12 : 4;
        var min = offset + 18;
        if (buffer.Count < min)
        {
            return false;
        }

        var expected = frameType == McFrameType.Frame4E ? "D400" : "D000";
        if (!StartsWithAscii(buffer, expected))
        {
            throw new ZeusProtocolException($"MC {frameType} ASCII 响应头错误。");
        }

        var responseLength = ReadAsciiHex(buffer, offset + 10, 4);
        if (responseLength < 4)
        {
            throw new ZeusProtocolException("MC ASCII 响应长度字段非法。");
        }

        var total = offset + 14 + responseLength;
        if (buffer.Count < total)
        {
            return false;
        }

        endCode = (ushort)ReadAsciiHex(buffer, offset + 14, 4);
        data = Copy(buffer, offset + 18, responseLength - 4);
        consumed = total;
        return true;
    }

    private static bool TryDecode1EBinaryResponse(
        IReadOnlyList<byte> buffer,
        McPendingRequest pending,
        out ushort endCode,
        out byte[] data,
        out int consumed)
    {
        endCode = 0;
        data = [];
        consumed = 0;
        if (buffer.Count < 2)
        {
            return false;
        }

        var expectedSubheader = (byte)(To1ECommand(pending.Operation) + 0x80);
        if (buffer[0] != expectedSubheader)
        {
            throw new ZeusProtocolException("MC 1E Binary 响应头错误。");
        }

        endCode = buffer[1];
        var extraErrorBytes = endCode == 0x5B ? 1 : 0;
        var expectedDataLength = endCode == 0 ? ExpectedBinaryResponseDataLength(pending) : extraErrorBytes;
        var total = 2 + expectedDataLength;
        if (buffer.Count < total)
        {
            return false;
        }

        if (endCode == 0 && IsReadOperation(pending.Operation))
        {
            data = DecodeDeviceResponseData(
                McDataEncoding.Binary,
                pending.Operation,
                pending.Points,
                pending.ExtraPoints,
                Copy(buffer, 2, expectedDataLength),
                true);
        }

        consumed = total;
        return true;
    }

    private static bool TryDecode1EAsciiResponse(
        IReadOnlyList<byte> buffer,
        McPendingRequest pending,
        out ushort endCode,
        out byte[] data,
        out int consumed)
    {
        endCode = 0;
        data = [];
        consumed = 0;
        if (buffer.Count < 4)
        {
            return false;
        }

        var expectedSubheader = To1ECommand(pending.Operation) + 0x80;
        if (ReadAsciiHex(buffer, 0, 2) != expectedSubheader)
        {
            throw new ZeusProtocolException("MC 1E ASCII 响应头错误。");
        }

        endCode = (ushort)ReadAsciiHex(buffer, 2, 2);
        var extraErrorBytes = endCode == 0x5B ? 2 : 0;
        var expectedDataLength = endCode == 0 ? ExpectedAsciiResponseDataLength(pending, true) : extraErrorBytes;
        var total = 4 + expectedDataLength;
        if (buffer.Count < total)
        {
            return false;
        }

        if (endCode == 0 && IsReadOperation(pending.Operation))
        {
            data = DecodeDeviceResponseData(
                McDataEncoding.Ascii,
                pending.Operation,
                pending.Points,
                pending.ExtraPoints,
                Copy(buffer, 4, expectedDataLength),
                true);
        }

        consumed = total;
        return true;
    }

    private static byte[] Encode1ERequest(Mc3EOptions options, McOperation operation, ReadOnlySpan<byte> canonicalData)
    {
        if (operation is McOperation.RandomRead or McOperation.RandomWriteWords or McOperation.RandomWriteBits)
        {
            throw new ZeusProtocolException("MC 1E 帧不支持随机读写，请改用 3E/4E 帧。");
        }

        return options.DataEncoding == McDataEncoding.Ascii
            ? Encode1EAsciiRequest(options, operation, canonicalData)
            : Encode1EBinaryRequest(options, operation, canonicalData);
    }

    private static byte[] Encode1EBinaryRequest(Mc3EOptions options, McOperation operation, ReadOnlySpan<byte> canonicalData)
    {
        var deviceData = Encode1EBinaryDeviceData(operation, canonicalData);
        var frame = new byte[4 + deviceData.Length];
        frame[0] = To1ECommand(operation);
        frame[1] = options.PcNumber;
        WriteUInt16LittleEndian(frame.AsSpan(2, 2), options.MonitoringTimer);
        deviceData.CopyTo(frame.AsSpan(4));
        return frame;
    }

    private static byte[] Encode1EAsciiRequest(Mc3EOptions options, McOperation operation, ReadOnlySpan<byte> canonicalData)
    {
        var deviceData = Encode1EAsciiDeviceData(operation, canonicalData);
        var frame = new byte[8 + deviceData.Length];
        WriteAsciiHex(frame, 0, To1ECommand(operation), 2);
        WriteAsciiHex(frame, 2, options.PcNumber, 2);
        WriteAsciiHex(frame, 4, options.MonitoringTimer, 4);
        deviceData.CopyTo(frame.AsSpan(8));
        return frame;
    }

    private static byte[] Encode1EResponse(McRequestContext context, ushort endCode, ReadOnlySpan<byte> data)
        => context.DataEncoding == McDataEncoding.Ascii
            ? Encode1EAsciiResponse(context, endCode, data)
            : Encode1EBinaryResponse(context, endCode, data);

    private static byte[] Encode1EBinaryResponse(McRequestContext context, ushort endCode, ReadOnlySpan<byte> data)
    {
        var responseData = endCode == 0 ? EncodeDeviceResponseData(context, data) : [];
        var endCodeByte = To1EEndCode(endCode);
        var extraErrorBytes = endCode != 0 && endCodeByte == 0x5B ? 1 : 0;
        var frame = new byte[2 + extraErrorBytes + responseData.Length];
        frame[0] = (byte)(context.OneECommand + 0x80);
        frame[1] = endCodeByte;
        if (extraErrorBytes == 1)
        {
            frame[2] = (byte)(endCode & 0xFF);
        }

        responseData.CopyTo(frame.AsSpan(2 + extraErrorBytes));
        return frame;
    }

    private static byte[] Encode1EAsciiResponse(McRequestContext context, ushort endCode, ReadOnlySpan<byte> data)
    {
        var responseData = endCode == 0 ? EncodeDeviceResponseData(context, data) : [];
        var endCodeByte = To1EEndCode(endCode);
        var extraErrorBytes = endCode != 0 && endCodeByte == 0x5B ? 2 : 0;
        var frame = new byte[4 + extraErrorBytes + responseData.Length];
        WriteAsciiHex(frame, 0, context.OneECommand + 0x80, 2);
        WriteAsciiHex(frame, 2, endCodeByte, 2);
        if (extraErrorBytes == 2)
        {
            WriteAsciiHex(frame, 4, endCode & 0xFF, 2);
        }

        responseData.CopyTo(frame.AsSpan(4 + extraErrorBytes));
        return frame;
    }

    private static byte[] EncodeDeviceData(Mc3EOptions options, McOperation operation, ReadOnlySpan<byte> canonicalData)
        => options.DataEncoding == McDataEncoding.Ascii
            ? Encode3EAsciiDeviceData(operation, canonicalData)
            : canonicalData.ToArray();

    private static byte[] Encode3EAsciiDeviceData(McOperation operation, ReadOnlySpan<byte> canonicalData)
    {
        if (operation == McOperation.Unknown)
        {
            return EncodeRawPayload(new Mc3EOptions { DataEncoding = McDataEncoding.Ascii }, canonicalData);
        }

        if (operation == McOperation.RandomRead)
        {
            return Encode3EAsciiRandomReadData(canonicalData);
        }

        if (operation == McOperation.RandomWriteWords)
        {
            return Encode3EAsciiRandomWriteWordsData(canonicalData);
        }

        if (operation == McOperation.RandomWriteBits)
        {
            return Encode3EAsciiRandomWriteBitsData(canonicalData);
        }

        var (address, deviceCode, points) = ReadDeviceRequest(canonicalData);
        var payload = canonicalData.Slice(6);
        var extra = operation switch
        {
            McOperation.WriteWords => points * 4,
            McOperation.WriteBits => points,
            _ => 0
        };
        var data = new byte[12 + extra];
        WriteAsciiDeviceAddress(data, 0, new McDeviceAddress(deviceCode, address));
        WriteAsciiHex(data, 8, points, 4);
        if (operation is McOperation.WriteWords or McOperation.WriteBits)
        {
            WriteAsciiPayload(data.AsSpan(12), operation, points, payload, false);
        }

        return data;
    }

    private static byte[] Decode3EAsciiDeviceData(McOperation operation, ReadOnlySpan<byte> asciiData)
    {
        if (operation == McOperation.Unknown)
        {
            if (asciiData.Length % 2 != 0)
            {
                throw new ZeusProtocolException("MC ASCII 原始命令数据长度必须为偶数。");
            }

            var binary = new byte[asciiData.Length / 2];
            for (var i = 0; i < binary.Length; i++)
            {
                binary[i] = (byte)ReadAsciiHex(asciiData, i * 2, 2);
            }

            return binary;
        }

        if (operation == McOperation.RandomRead)
        {
            return Decode3EAsciiRandomReadData(asciiData);
        }

        if (operation == McOperation.RandomWriteWords)
        {
            return Decode3EAsciiRandomWriteWordsData(asciiData);
        }

        if (operation == McOperation.RandomWriteBits)
        {
            return Decode3EAsciiRandomWriteBitsData(asciiData);
        }

        if (asciiData.Length < 12)
        {
            throw new ZeusProtocolException("MC ASCII 软元件请求长度不足。");
        }

        var device = ReadAsciiDeviceAddress(asciiData, 0);
        var points = (ushort)ReadAsciiHex(asciiData, 8, 4);
        var result = BuildDeviceRequest(device.Address, device.DeviceCode, points);
        var payload = operation is McOperation.WriteWords or McOperation.WriteBits
            ? DecodeAsciiPayload(operation, points, asciiData.Slice(12), false)
            : [];
        return Concat(result, payload);
    }

    private static byte[] Encode3EAsciiRandomReadData(ReadOnlySpan<byte> canonicalData)
    {
        var (wordDevices, doubleWordDevices) = ReadRandomReadRequest(canonicalData);
        var data = new byte[4 + ((wordDevices.Length + doubleWordDevices.Length) * 8)];
        WriteAsciiHex(data, 0, wordDevices.Length, 2);
        WriteAsciiHex(data, 2, doubleWordDevices.Length, 2);
        var offset = 4;
        foreach (var device in wordDevices)
        {
            WriteAsciiDeviceAddress(data, offset, device);
            offset += 8;
        }

        foreach (var device in doubleWordDevices)
        {
            WriteAsciiDeviceAddress(data, offset, device);
            offset += 8;
        }

        return data;
    }

    private static byte[] Decode3EAsciiRandomReadData(ReadOnlySpan<byte> asciiData)
    {
        if (asciiData.Length < 4)
        {
            throw new ZeusProtocolException("MC ASCII 随机读取请求长度不足。");
        }

        var wordCount = ReadAsciiHex(asciiData, 0, 2);
        var doubleWordCount = ReadAsciiHex(asciiData, 2, 2);
        var expected = 4 + ((wordCount + doubleWordCount) * 8);
        if (asciiData.Length < expected)
        {
            throw new ZeusProtocolException("MC ASCII 随机读取软元件列表长度不足。");
        }

        var data = new byte[2 + ((wordCount + doubleWordCount) * 4)];
        data[0] = (byte)wordCount;
        data[1] = (byte)doubleWordCount;
        var asciiOffset = 4;
        var offset = 2;
        for (var i = 0; i < wordCount + doubleWordCount; i++)
        {
            var device = ReadAsciiDeviceAddress(asciiData, asciiOffset);
            WriteDeviceAddress(data.AsSpan(offset, 4), device);
            asciiOffset += 8;
            offset += 4;
        }

        return data;
    }

    private static byte[] Encode3EAsciiRandomWriteWordsData(ReadOnlySpan<byte> canonicalData)
    {
        var (wordValues, doubleWordValues) = ReadRandomWriteWordsRequest(canonicalData);
        var data = new byte[4 + (wordValues.Length * 12) + (doubleWordValues.Length * 16)];
        WriteAsciiHex(data, 0, wordValues.Length, 2);
        WriteAsciiHex(data, 2, doubleWordValues.Length, 2);
        var offset = 4;
        foreach (var item in wordValues)
        {
            WriteAsciiDeviceAddress(data, offset, new McDeviceAddress(item.DeviceCode, item.Address));
            WriteAsciiHex(data, offset + 8, item.Value, 4);
            offset += 12;
        }

        foreach (var item in doubleWordValues)
        {
            WriteAsciiDeviceAddress(data, offset, new McDeviceAddress(item.DeviceCode, item.Address));
            WriteAsciiHex(data, offset + 8, item.Value, 8);
            offset += 16;
        }

        return data;
    }

    private static byte[] Decode3EAsciiRandomWriteWordsData(ReadOnlySpan<byte> asciiData)
    {
        if (asciiData.Length < 4)
        {
            throw new ZeusProtocolException("MC ASCII 随机写入字软元件请求长度不足。");
        }

        var wordCount = ReadAsciiHex(asciiData, 0, 2);
        var doubleWordCount = ReadAsciiHex(asciiData, 2, 2);
        var expected = 4 + (wordCount * 12) + (doubleWordCount * 16);
        if (asciiData.Length < expected)
        {
            throw new ZeusProtocolException("MC ASCII 随机写入字软元件数据长度不足。");
        }

        var data = new byte[2 + (wordCount * 6) + (doubleWordCount * 8)];
        data[0] = (byte)wordCount;
        data[1] = (byte)doubleWordCount;
        var asciiOffset = 4;
        var offset = 2;
        for (var i = 0; i < wordCount; i++)
        {
            var device = ReadAsciiDeviceAddress(asciiData, asciiOffset);
            WriteDeviceAddress(data.AsSpan(offset, 4), device);
            WriteUInt16LittleEndian(data.AsSpan(offset + 4, 2), (ushort)ReadAsciiHex(asciiData, asciiOffset + 8, 4));
            asciiOffset += 12;
            offset += 6;
        }

        for (var i = 0; i < doubleWordCount; i++)
        {
            var device = ReadAsciiDeviceAddress(asciiData, asciiOffset);
            WriteDeviceAddress(data.AsSpan(offset, 4), device);
            WriteUInt32LittleEndian(data.AsSpan(offset + 4, 4), ReadAsciiUInt32Hex(asciiData, asciiOffset + 8, 8));
            asciiOffset += 16;
            offset += 8;
        }

        return data;
    }

    private static byte[] Encode3EAsciiRandomWriteBitsData(ReadOnlySpan<byte> canonicalData)
    {
        var values = ReadRandomWriteBitsRequest(canonicalData);
        var data = new byte[2 + (values.Length * 10)];
        WriteAsciiHex(data, 0, values.Length, 2);
        var offset = 2;
        foreach (var item in values)
        {
            WriteAsciiDeviceAddress(data, offset, new McDeviceAddress(item.DeviceCode, item.Address));
            WriteAsciiHex(data, offset + 8, item.Value ? 1 : 0, 2);
            offset += 10;
        }

        return data;
    }

    private static byte[] Decode3EAsciiRandomWriteBitsData(ReadOnlySpan<byte> asciiData)
    {
        if (asciiData.Length < 2)
        {
            throw new ZeusProtocolException("MC ASCII 随机写入位软元件请求长度不足。");
        }

        var count = ReadAsciiHex(asciiData, 0, 2);
        var expected = 2 + (count * 10);
        if (asciiData.Length < expected)
        {
            throw new ZeusProtocolException("MC ASCII 随机写入位软元件数据长度不足。");
        }

        var data = new byte[1 + (count * 5)];
        data[0] = (byte)count;
        var asciiOffset = 2;
        var offset = 1;
        for (var i = 0; i < count; i++)
        {
            var device = ReadAsciiDeviceAddress(asciiData, asciiOffset);
            WriteDeviceAddress(data.AsSpan(offset, 4), device);
            data[offset + 4] = ReadAsciiHex(asciiData, asciiOffset + 8, 2) == 0 ? (byte)0 : (byte)1;
            asciiOffset += 10;
            offset += 5;
        }

        return data;
    }

    private static byte[] Encode1EBinaryDeviceData(McOperation operation, ReadOnlySpan<byte> canonicalData)
    {
        var (address, deviceCode, points) = ReadDeviceRequest(canonicalData);
        var payload = canonicalData.Slice(6);
        var extra = operation switch
        {
            McOperation.WriteWords => points * 2,
            McOperation.WriteBits => BitByteCount(points),
            _ => 0
        };
        var data = new byte[8 + extra];
        data[0] = (byte)(address & 0xFF);
        data[1] = (byte)((address >> 8) & 0xFF);
        data[2] = (byte)((address >> 16) & 0xFF);
        data[3] = (byte)((address >> 24) & 0xFF);
        WriteUInt16LittleEndian(data.AsSpan(4, 2), To1EDeviceCode(deviceCode));
        data[6] = To1EPointCount(points);
        data[7] = 0x00;
        payload.CopyTo(data.AsSpan(8));
        return data;
    }

    private static byte[] Decode1EBinaryDeviceData(McOperation operation, ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            throw new ZeusProtocolException("MC 1E Binary 软元件请求长度不足。");
        }

        var address = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
        var deviceCode = From1EDeviceCode(ReadUInt16LittleEndian(data.Slice(4, 2)));
        var points = From1EPointCount(data[6]);
        var result = BuildDeviceRequest(address, deviceCode, points);
        var payloadLength = operation switch
        {
            McOperation.WriteWords => points * 2,
            McOperation.WriteBits => BitByteCount(points),
            _ => 0
        };
        if (data.Length < 8 + payloadLength)
        {
            throw new ZeusProtocolException("MC 1E Binary 写入数据长度不足。");
        }

        return Concat(result, data.Slice(8, payloadLength).ToArray());
    }

    private static byte[] Encode1EAsciiDeviceData(McOperation operation, ReadOnlySpan<byte> canonicalData)
    {
        var (address, deviceCode, points) = ReadDeviceRequest(canonicalData);
        var payload = canonicalData.Slice(6);
        var extra = operation switch
        {
            McOperation.WriteWords => points * 4,
            McOperation.WriteBits => RoundEven(points),
            _ => 0
        };
        var data = new byte[16 + extra];
        WriteAsciiHex(data, 0, To1EDeviceCode(deviceCode), 4);
        WriteAsciiHex(data, 4, address, 8);
        WriteAsciiHex(data, 12, To1EPointCount(points), 2);
        WriteAsciiHex(data, 14, 0, 2);
        if (operation is McOperation.WriteWords or McOperation.WriteBits)
        {
            WriteAsciiPayload(data.AsSpan(16), operation, points, payload, true);
        }

        return data;
    }

    private static byte[] Decode1EAsciiDeviceData(McOperation operation, ReadOnlySpan<byte> asciiData)
    {
        if (asciiData.Length < 16)
        {
            throw new ZeusProtocolException("MC 1E ASCII 软元件请求长度不足。");
        }

        var deviceCode = From1EDeviceCode((ushort)ReadAsciiHex(asciiData, 0, 4));
        var address = ReadAsciiHex(asciiData, 4, 8);
        var points = From1EPointCount((byte)ReadAsciiHex(asciiData, 12, 2));
        var result = BuildDeviceRequest(address, deviceCode, points);
        var payload = operation is McOperation.WriteWords or McOperation.WriteBits
            ? DecodeAsciiPayload(operation, points, asciiData.Slice(16), true)
            : [];
        return Concat(result, payload);
    }

    private static byte[] EncodeDeviceResponseData(McRequestContext context, ReadOnlySpan<byte> data)
        => context.DataEncoding == McDataEncoding.Ascii
            ? EncodeAsciiResponsePayload(context.Operation, context.Points, context.ExtraPoints, data, context.FrameType == McFrameType.Frame1E)
            : data.ToArray();

    private static byte[] DecodeDeviceResponseData(
        McDataEncoding encoding,
        McOperation operation,
        ushort points,
        ushort extraPoints,
        ReadOnlySpan<byte> raw,
        bool oneE)
        => encoding == McDataEncoding.Ascii
            ? DecodeAsciiResponsePayload(operation, points, extraPoints, raw, oneE)
            : raw.ToArray();

    private static byte[] EncodeAsciiResponsePayload(
        McOperation operation,
        ushort points,
        ushort extraPoints,
        ReadOnlySpan<byte> payload,
        bool oneE)
    {
        if (!IsReadOperation(operation))
        {
            // 远程控制等无数据命令保持空载荷；多块读取等原始命令把二进制编成 ASCII 十六进制。
            return operation == McOperation.Unknown && payload.Length > 0
                ? EncodeRawPayload(new Mc3EOptions { DataEncoding = McDataEncoding.Ascii }, payload)
                : [];
        }

        if (operation == McOperation.RandomRead)
        {
            return EncodeAsciiRandomReadPayload(points, extraPoints, payload);
        }

        var data = new byte[operation switch
        {
            McOperation.ReadWords => points * 4,
            McOperation.ReadBits => oneE ? RoundEven(points) : points,
            _ => 0
        }];
        WriteAsciiPayload(data, operation, points, payload, oneE);
        return data;
    }

    private static byte[] DecodeAsciiResponsePayload(
        McOperation operation,
        ushort points,
        ushort extraPoints,
        ReadOnlySpan<byte> payload,
        bool oneE)
        => operation == McOperation.RandomRead
            ? DecodeAsciiRandomReadPayload(points, extraPoints, payload)
            : DecodeAsciiPayload(operation, points, payload, oneE);

    private static byte[] EncodeAsciiRandomReadPayload(ushort wordCount, ushort doubleWordCount, ReadOnlySpan<byte> payload)
    {
        var expected = (wordCount * 2) + (doubleWordCount * 4);
        if (payload.Length < expected)
        {
            throw new ZeusProtocolException("MC 随机读取响应数据长度不足。");
        }

        var data = new byte[(wordCount * 4) + (doubleWordCount * 8)];
        var sourceOffset = 0;
        var targetOffset = 0;
        for (var i = 0; i < wordCount; i++)
        {
            WriteAsciiHex(data, targetOffset, ReadUInt16LittleEndian(payload.Slice(sourceOffset, 2)), 4);
            sourceOffset += 2;
            targetOffset += 4;
        }

        for (var i = 0; i < doubleWordCount; i++)
        {
            WriteAsciiHex(data, targetOffset, ReadUInt32LittleEndian(payload.Slice(sourceOffset, 4)), 8);
            sourceOffset += 4;
            targetOffset += 8;
        }

        return data;
    }

    private static byte[] DecodeAsciiRandomReadPayload(ushort wordCount, ushort doubleWordCount, ReadOnlySpan<byte> payload)
    {
        var expected = (wordCount * 4) + (doubleWordCount * 8);
        if (payload.Length < expected)
        {
            throw new ZeusProtocolException("MC ASCII 随机读取响应数据长度不足。");
        }

        var data = new byte[(wordCount * 2) + (doubleWordCount * 4)];
        var sourceOffset = 0;
        var targetOffset = 0;
        for (var i = 0; i < wordCount; i++)
        {
            WriteUInt16LittleEndian(data.AsSpan(targetOffset, 2), (ushort)ReadAsciiHex(payload, sourceOffset, 4));
            sourceOffset += 4;
            targetOffset += 2;
        }

        for (var i = 0; i < doubleWordCount; i++)
        {
            WriteUInt32LittleEndian(data.AsSpan(targetOffset, 4), ReadAsciiUInt32Hex(payload, sourceOffset, 8));
            sourceOffset += 8;
            targetOffset += 4;
        }

        return data;
    }

    private static void WriteAsciiPayload(Span<byte> destination, McOperation operation, ushort points, ReadOnlySpan<byte> payload, bool oneE)
    {
        if (operation is McOperation.ReadWords or McOperation.WriteWords)
        {
            for (var i = 0; i < points; i++)
            {
                WriteAsciiHex(destination, i * 4, ReadUInt16LittleEndian(payload.Slice(i * 2, 2)), 4);
            }
        }
        else if (operation is McOperation.ReadBits or McOperation.WriteBits)
        {
            for (var i = 0; i < points; i++)
            {
                destination[i] = GetPackedBit(payload, i) ? (byte)'1' : (byte)'0';
            }

            if (oneE && points % 2 != 0 && destination.Length > points)
            {
                destination[points] = (byte)'0';
            }
        }
    }

    private static byte[] DecodeAsciiPayload(McOperation operation, ushort points, ReadOnlySpan<byte> payload, bool oneE)
    {
        if (operation is McOperation.ReadWords or McOperation.WriteWords)
        {
            if (payload.Length < points * 4)
            {
                throw new ZeusProtocolException("MC ASCII 字软元件数据长度不足。");
            }

            var data = new byte[points * 2];
            for (var i = 0; i < points; i++)
            {
                WriteUInt16LittleEndian(data.AsSpan(i * 2, 2), (ushort)ReadAsciiHex(payload, i * 4, 4));
            }

            return data;
        }

        if (operation is McOperation.ReadBits or McOperation.WriteBits)
        {
            var expected = oneE ? RoundEven(points) : points;
            if (payload.Length < expected)
            {
                throw new ZeusProtocolException("MC ASCII 位软元件数据长度不足。");
            }

            var data = new byte[BitByteCount(points)];
            for (var i = 0; i < points; i++)
            {
                SetPackedBit(data, i, payload[i] == '1');
            }

            return data;
        }

        return payload.ToArray();
    }

    private static int ExpectedBinaryResponseDataLength(McPendingRequest pending)
        => pending.Operation switch
        {
            McOperation.ReadWords => pending.Points * 2,
            McOperation.ReadBits => BitByteCount(pending.Points),
            McOperation.RandomRead => (pending.Points * 2) + (pending.ExtraPoints * 4),
            _ => 0
        };

    private static int ExpectedAsciiResponseDataLength(McPendingRequest pending, bool oneE)
        => pending.Operation switch
        {
            McOperation.ReadWords => pending.Points * 4,
            McOperation.ReadBits => oneE ? RoundEven(pending.Points) : pending.Points,
            McOperation.RandomRead => (pending.Points * 4) + (pending.ExtraPoints * 8),
            _ => 0
        };

    private static (ushort Command, ushort Subcommand) ToCommand(McOperation operation)
        => operation switch
        {
            McOperation.ReadWords => (BatchReadCommand, WordSubcommand),
            McOperation.WriteWords => (BatchWriteCommand, WordSubcommand),
            McOperation.ReadBits => (BatchReadCommand, BitSubcommand),
            McOperation.WriteBits => (BatchWriteCommand, BitSubcommand),
            McOperation.RandomRead => (RandomReadCommand, WordSubcommand),
            McOperation.RandomWriteWords => (RandomWriteCommand, WordSubcommand),
            McOperation.RandomWriteBits => (RandomWriteCommand, BitSubcommand),
            _ => (0, 0)
        };

    private static bool IsReadOperation(McOperation operation)
        => operation is McOperation.ReadWords or McOperation.ReadBits or McOperation.RandomRead;

    private static byte To1ECommand(McOperation operation)
        => operation switch
        {
            McOperation.ReadBits => OneEReadBits,
            McOperation.ReadWords => OneEReadWords,
            McOperation.WriteBits => OneEWriteBits,
            McOperation.WriteWords => OneEWriteWords,
            _ => throw new ZeusProtocolException("MC 1E 不支持该操作。")
        };

    private static McOperation To1EOperation(byte command)
        => command switch
        {
            OneEReadBits => McOperation.ReadBits,
            OneEReadWords => McOperation.ReadWords,
            OneEWriteBits => McOperation.WriteBits,
            OneEWriteWords => McOperation.WriteWords,
            _ => McOperation.Unknown
        };

    private static bool Is1ECommand(byte command)
        => command is OneEReadBits or OneEReadWords or OneEWriteBits or OneEWriteWords;

    private static byte To1EPointCount(ushort points)
    {
        if (points is 0 or > 256)
        {
            throw new ZeusProtocolException($"MC 1E 批量读写点数必须在 1 到 256 之间，当前为 {points}。");
        }

        return points == 256 ? (byte)0 : (byte)points;
    }

    private static ushort From1EPointCount(byte points)
        => points == 0 ? (ushort)256 : points;

    private static ushort To1EDeviceCode(McDeviceCode deviceCode)
    {
        return deviceCode switch
        {
            McDeviceCode.InputRelay => 0x5820,
            McDeviceCode.OutputRelay => 0x5920,
            McDeviceCode.InternalRelay => 0x4D20,
            McDeviceCode.DataRegister => 0x4420,
            McDeviceCode.LinkRegister => 0x5720,
            McDeviceCode.FileRegister => 0x5220,
            _ => throw new ZeusProtocolException($"MC 1E 暂不支持软元件 {deviceCode}。")
        };
    }

    private static McDeviceCode From1EDeviceCode(ushort value)
        => value switch
        {
            0x5820 => McDeviceCode.InputRelay,
            0x5920 => McDeviceCode.OutputRelay,
            0x4D20 => McDeviceCode.InternalRelay,
            0x4420 => McDeviceCode.DataRegister,
            0x5720 => McDeviceCode.LinkRegister,
            0x5220 => McDeviceCode.FileRegister,
            _ => (McDeviceCode)0
        };

    private static byte To1EEndCode(ushort endCode)
        => endCode == 0 ? (byte)0 : endCode <= byte.MaxValue ? (byte)endCode : (byte)0x5B;

    private static (ushort Points, ushort ExtraPoints) TryReadPointCounts(McOperation operation, ReadOnlySpan<byte> data)
        => operation switch
        {
            McOperation.RandomRead or McOperation.RandomWriteWords when data.Length >= 2 => (data[0], data[1]),
            McOperation.RandomWriteBits when data.Length >= 1 => (data[0], 0),
            _ => (data.Length >= 6 ? ReadUInt16LittleEndian(data.Slice(4, 2)) : (ushort)0, 0)
        };

    private static void WriteBinaryRoute(Span<byte> destination, Mc3EOptions options)
    {
        destination[0] = options.NetworkNumber;
        destination[1] = options.PcNumber;
        WriteUInt16LittleEndian(destination.Slice(2, 2), options.IoNumber);
        destination[4] = options.StationNumber;
    }

    private static void WriteBinaryRoute(Span<byte> destination, McRequestContext context)
    {
        destination[0] = context.NetworkNumber;
        destination[1] = context.PcNumber;
        WriteUInt16LittleEndian(destination.Slice(2, 2), context.IoNumber);
        destination[4] = context.StationNumber;
    }

    private static void WriteAsciiRoute(Span<byte> destination, Mc3EOptions options)
    {
        WriteAsciiHex(destination, 0, options.NetworkNumber, 2);
        WriteAsciiHex(destination, 2, options.PcNumber, 2);
        WriteAsciiHex(destination, 4, options.IoNumber, 4);
        WriteAsciiHex(destination, 8, options.StationNumber, 2);
    }

    private static void WriteAsciiRoute(Span<byte> destination, McRequestContext context)
    {
        WriteAsciiHex(destination, 0, context.NetworkNumber, 2);
        WriteAsciiHex(destination, 2, context.PcNumber, 2);
        WriteAsciiHex(destination, 4, context.IoNumber, 4);
        WriteAsciiHex(destination, 8, context.StationNumber, 2);
    }

    private static void WriteAsciiDeviceCode(Span<byte> destination, int offset, McDeviceCode deviceCode)
    {
        var code = deviceCode switch
        {
            McDeviceCode.InputRelay => "X*",
            McDeviceCode.OutputRelay => "Y*",
            McDeviceCode.InternalRelay => "M*",
            McDeviceCode.DataRegister => "D*",
            McDeviceCode.LinkRegister => "W*",
            McDeviceCode.FileRegister => "R*",
            McDeviceCode.ExtendedFileRegister => "ZR",
            _ => throw new ZeusProtocolException($"不支持的 MC 软元件代码：{deviceCode}。")
        };
        WriteAsciiText(destination, offset, code);
    }

    private static void WriteAsciiDeviceAddress(Span<byte> destination, int offset, McDeviceAddress device)
    {
        WriteAsciiDeviceCode(destination, offset, device.DeviceCode);
        WriteAsciiAddress(destination, offset + 2, device.Address, device.DeviceCode, 6);
    }

    private static McDeviceAddress ReadAsciiDeviceAddress(ReadOnlySpan<byte> source, int offset)
    {
        if (source.Length < offset + 8)
        {
            throw new ZeusProtocolException("MC ASCII 软元件地址长度不足。");
        }

        var deviceCode = ReadAsciiDeviceCode(source.Slice(offset, 2));
        var address = ReadAsciiAddress(source.Slice(offset + 2, 6), deviceCode);
        return new McDeviceAddress(deviceCode, address);
    }

    private static McDeviceCode ReadAsciiDeviceCode(ReadOnlySpan<byte> code)
    {
        if (code.Length < 2)
        {
            throw new ZeusProtocolException("MC ASCII 软元件代码长度不足。");
        }

        return ((char)code[0], (char)code[1]) switch
        {
            ('X', '*') or ('X', ' ') => McDeviceCode.InputRelay,
            ('Y', '*') or ('Y', ' ') => McDeviceCode.OutputRelay,
            ('M', '*') or ('M', ' ') => McDeviceCode.InternalRelay,
            ('D', '*') or ('D', ' ') => McDeviceCode.DataRegister,
            ('W', '*') or ('W', ' ') => McDeviceCode.LinkRegister,
            ('R', '*') or ('R', ' ') => McDeviceCode.FileRegister,
            ('Z', 'R') => McDeviceCode.ExtendedFileRegister,
            _ => (McDeviceCode)0
        };
    }

    private static void WriteAsciiAddress(Span<byte> destination, int offset, int address, McDeviceCode deviceCode, int digits)
    {
        var text = UsesHexAddress(deviceCode)
            ? address.ToString($"X{digits}", System.Globalization.CultureInfo.InvariantCulture)
            : address.ToString($"D{digits}", System.Globalization.CultureInfo.InvariantCulture);
        if (text.Length > digits)
        {
            throw new ZeusProtocolException($"MC ASCII 软元件地址超出 {digits} 位：{address}。");
        }

        WriteAsciiText(destination, offset, text);
    }

    private static int ReadAsciiAddress(ReadOnlySpan<byte> value, McDeviceCode deviceCode)
        => UsesHexAddress(deviceCode)
            ? ReadAsciiHex(value, 0, value.Length)
            : ReadAsciiDecimal(value);

    private static bool UsesHexAddress(McDeviceCode deviceCode)
        => deviceCode is McDeviceCode.InputRelay or McDeviceCode.OutputRelay or McDeviceCode.LinkRegister;

    private static void WriteAsciiHex(Span<byte> destination, int offset, int value, int digits)
    {
        for (var i = digits - 1; i >= 0; i--)
        {
            destination[offset + i] = ToHexByte(value & 0x0F);
            value >>= 4;
        }
    }

    private static void WriteAsciiHex(Span<byte> destination, int offset, uint value, int digits)
    {
        for (var i = digits - 1; i >= 0; i--)
        {
            destination[offset + i] = ToHexByte((int)(value & 0x0F));
            value >>= 4;
        }
    }

    private static int ReadAsciiHex(IReadOnlyList<byte> source, int offset, int digits)
    {
        if (source.Count < offset + digits)
        {
            throw new ZeusProtocolException("MC ASCII 十六进制字段长度不足。");
        }

        var value = 0;
        for (var i = 0; i < digits; i++)
        {
            value = (value << 4) | FromHexByte(source[offset + i]);
        }

        return value;
    }

    private static int ReadAsciiHex(ReadOnlySpan<byte> source, int offset, int digits)
    {
        if (source.Length < offset + digits)
        {
            throw new ZeusProtocolException("MC ASCII 十六进制字段长度不足。");
        }

        var value = 0;
        for (var i = 0; i < digits; i++)
        {
            value = (value << 4) | FromHexByte(source[offset + i]);
        }

        return value;
    }

    private static uint ReadAsciiUInt32Hex(ReadOnlySpan<byte> source, int offset, int digits)
    {
        if (source.Length < offset + digits)
        {
            throw new ZeusProtocolException("MC ASCII 十六进制字段长度不足。");
        }

        uint value = 0;
        for (var i = 0; i < digits; i++)
        {
            value = (value << 4) | (uint)FromHexByte(source[offset + i]);
        }

        return value;
    }

    private static int ReadAsciiDecimal(ReadOnlySpan<byte> source)
    {
        var value = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] is < (byte)'0' or > (byte)'9')
            {
                throw new ZeusProtocolException("MC ASCII 十进制字段包含非法字符。");
            }

            value = (value * 10) + (source[i] - '0');
        }

        return value;
    }

    private static byte ToHexByte(int value)
        => (byte)(value < 10 ? '0' + value : 'A' + value - 10);

    private static int FromHexByte(byte value)
        => value switch
        {
            >= (byte)'0' and <= (byte)'9' => value - '0',
            >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
            >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
            _ => throw new ZeusProtocolException("MC ASCII 十六进制字段包含非法字符。")
        };

    private static void WriteAsciiText(Span<byte> destination, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            destination[offset + i] = (byte)text[i];
        }
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> source, string text)
    {
        if (source.Length < text.Length)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (source[i] != text[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWithAscii(IReadOnlyList<byte> source, string text)
    {
        if (source.Count < text.Length)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (source[i] != text[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiHex(ReadOnlySpan<byte> source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (!((source[i] >= '0' && source[i] <= '9')
                || (source[i] >= 'A' && source[i] <= 'F')
                || (source[i] >= 'a' && source[i] <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static int RoundEven(int value)
        => value % 2 == 0 ? value : value + 1;

    private static void ValidateAddress(int address)
    {
        if (address is < 0 or > 0xFFFFFF)
        {
            throw new ZeusProtocolException($"MC 软元件地址必须在 0 到 16777215 之间，当前为 {address}。");
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

    private static byte[] Concat(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var result = new byte[first.Length + second.Length];
        first.CopyTo(result);
        second.CopyTo(result.AsSpan(first.Length));
        return result;
    }
}

internal enum McOperation
{
    Unknown,
    ReadBits,
    ReadWords,
    WriteBits,
    WriteWords,
    RandomRead,
    RandomWriteWords,
    RandomWriteBits
}

internal readonly record struct McPendingRequest(McOperation Operation, ushort Points, ushort ExtraPoints);

internal readonly record struct McRequestContext(
    McFrameType FrameType,
    McDataEncoding DataEncoding,
    byte NetworkNumber,
    byte PcNumber,
    ushort IoNumber,
    byte StationNumber,
    ushort SerialNumber,
    McOperation Operation,
    ushort Points,
    ushort ExtraPoints,
    ushort Command,
    ushort Subcommand,
    byte OneECommand);
