namespace Zeus;

/// <summary>
/// Omron FINS 虚拟 PLC。实现 <see cref="IVirtualResponder"/>，可直接交给 <c>AddVirtualChannel</c>。
/// </summary>
public sealed class FinsSlaveResponder : IVirtualResponder
{
    private const ushort Success = 0x0000;
    private const ushort UnsupportedCommand = 0x0402;
    private const ushort BadFormat = 0x1004;
    private const ushort InvalidArea = 0x1101;
    private const ushort AddressOutOfRange = 0x1103;
    private readonly FinsTransport _transport;
    private readonly FinsOptions _options;
    private readonly FinsSlaveMemory _memory;

    /// <summary>创建虚拟 PLC。</summary>
    public FinsSlaveResponder(FinsTransport transport = FinsTransport.Udp, FinsSlaveMemory? memory = null, FinsOptions? options = null)
    {
        _transport = transport;
        _memory = memory ?? new FinsSlaveMemory();
        _options = CopyOptions(options ?? new FinsOptions { DestinationNode = 1, SourceNode = 2 });
    }

    /// <summary>可在测试中预置或断言的映像。</summary>
    public FinsSlaveMemory Memory => _memory;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Respond(ReadOnlyMemory<byte> request)
    {
        if (_transport == FinsTransport.Tcp)
        {
            return RespondTcp(request);
        }

        return RespondFinsFrame(request.Span);
    }

    private ReadOnlyMemory<byte>? RespondTcp(ReadOnlyMemory<byte> request)
    {
        if (!FinsCodec.TryDecodeTcpPacket(request.ToArray(), out var command, out var error, out var payload, out _))
        {
            return null;
        }

        if (error != 0)
        {
            return FinsCodec.EncodeTcpPacket(command, error, []);
        }

        if (command == FinsCodec.TcpCommandNodeAddressDataSend)
        {
            var requested = payload.Length >= 4 ? (byte)FinsCodec.ReadUInt32BigEndian(payload.AsSpan(0, 4)) : (byte)0;
            var clientNode = requested != 0 ? requested : _options.SourceNode == 0 ? (byte)2 : _options.SourceNode;
            var serverNode = _options.DestinationNode == 0 ? (byte)1 : _options.DestinationNode;
            var data = new byte[8];
            FinsCodec.WriteUInt32BigEndian(data.AsSpan(0, 4), clientNode);
            FinsCodec.WriteUInt32BigEndian(data.AsSpan(4, 4), serverNode);
            return FinsCodec.EncodeTcpPacket(FinsCodec.TcpCommandNodeAddressDataSendResponse, 0, data);
        }

        if (command != FinsCodec.TcpCommandFinsFrameSend)
        {
            return FinsCodec.EncodeTcpPacket(FinsCodec.TcpCommandFinsFrameSendErrorNotification, 0x00000003, []);
        }

        var response = RespondFinsFrame(payload);
        return response is null
            ? null
            : FinsCodec.EncodeTcpPacket(FinsCodec.TcpCommandFinsFrameSend, 0, response.Value.Span);
    }

    private ReadOnlyMemory<byte>? RespondFinsFrame(ReadOnlySpan<byte> frame)
    {
        if (!FinsCodec.TryDecodeRequestFrame(frame, out var context, out var data))
        {
            return null;
        }

        try
        {
            var response = Handle(context.Command, data);
            return FinsCodec.EncodeResponseFrame(context, Success, response);
        }
        catch (FinsSlaveException ex)
        {
            return FinsCodec.EncodeResponseFrame(context, ex.EndCode, []);
        }
        catch (Exception)
        {
            return FinsCodec.EncodeResponseFrame(context, BadFormat, []);
        }
    }

    private byte[] Handle(ushort command, byte[] data)
        => command switch
        {
            FinsCodec.MemoryAreaRead => MemoryAreaRead(data),
            FinsCodec.MemoryAreaWrite => MemoryAreaWrite(data),
            FinsCodec.MemoryAreaFill => MemoryAreaFill(data),
            FinsCodec.MultipleMemoryAreaRead => MultipleMemoryAreaRead(data),
            _ => throw new FinsSlaveException(UnsupportedCommand)
        };

    private byte[] MemoryAreaRead(byte[] data)
    {
        var (area, address, bitOffset, count) = ReadAreaHeader(data);
        if (IsBitArea(area))
        {
            var result = new byte[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = ReadBit(area, address, bitOffset, i) ? (byte)1 : (byte)0;
            }

            return result;
        }

        var table = GetWordTable(area);
        EnsureWordRange(address, count, table.Length);
        var response = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            FinsCodec.WriteUInt16BigEndian(response.AsSpan(i * 2, 2), table[address + i]);
        }

        return response;
    }

    private byte[] MemoryAreaWrite(byte[] data)
    {
        var (area, address, bitOffset, count) = ReadAreaHeader(data);
        if (IsBitArea(area))
        {
            if (data.Length < 6 + count)
            {
                throw new FinsSlaveException(BadFormat);
            }

            for (var i = 0; i < count; i++)
            {
                WriteBit(area, address, bitOffset, i, data[6 + i] != 0);
            }

            return [];
        }

        if (data.Length < 6 + (count * 2))
        {
            throw new FinsSlaveException(BadFormat);
        }

        var table = GetWordTable(area);
        EnsureWordRange(address, count, table.Length);
        for (var i = 0; i < count; i++)
        {
            table[address + i] = FinsCodec.ReadUInt16BigEndian(data.AsSpan(6 + (i * 2), 2));
        }

        return [];
    }

    private byte[] MemoryAreaFill(byte[] data)
    {
        var (area, address, _, count) = ReadAreaHeader(data);
        if (data.Length < 8 || IsBitArea(area))
        {
            throw new FinsSlaveException(BadFormat);
        }

        var value = FinsCodec.ReadUInt16BigEndian(data.AsSpan(6, 2));
        var table = GetWordTable(area);
        EnsureWordRange(address, count, table.Length);
        for (var i = 0; i < count; i++)
        {
            table[address + i] = value;
        }

        return [];
    }

    private byte[] MultipleMemoryAreaRead(byte[] data)
    {
        if (data.Length == 0 || data.Length % 4 != 0)
        {
            throw new FinsSlaveException(BadFormat);
        }

        var output = new List<byte>(data.Length);
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var area = data[offset];
            var address = FinsCodec.ReadUInt16BigEndian(data.AsSpan(offset + 1, 2));
            var bit = data[offset + 3];
            if (IsBitArea(area))
            {
                output.Add(ReadBit(area, address, bit, 0) ? (byte)1 : (byte)0);
                continue;
            }

            var table = GetWordTable(area);
            EnsureWordRange(address, 1, table.Length);
            var bytes = new byte[2];
            FinsCodec.WriteUInt16BigEndian(bytes, table[address]);
            output.AddRange(bytes);
        }

        return output.ToArray();
    }

    private (byte Area, ushort Address, byte BitOffset, ushort Count) ReadAreaHeader(byte[] data)
    {
        if (data.Length < 6)
        {
            throw new FinsSlaveException(BadFormat);
        }

        var count = FinsCodec.ReadUInt16BigEndian(data.AsSpan(4, 2));
        if (count == 0)
        {
            throw new FinsSlaveException(BadFormat);
        }

        return (data[0], FinsCodec.ReadUInt16BigEndian(data.AsSpan(1, 2)), data[3], count);
    }

    private bool ReadBit(byte area, int address, byte bitOffset, int offset)
    {
        var absoluteBit = address * 16 + bitOffset + offset;
        if (area == FinsMemoryAreaCode.TimerCounterFlag.Code)
        {
            EnsureBitRange(absoluteBit, _memory.TimerCounterFlags.Length);
            return _memory.TimerCounterFlags[absoluteBit];
        }

        var table = GetWordTable(area);
        var word = absoluteBit / 16;
        var bit = absoluteBit % 16;
        EnsureWordRange(word, 1, table.Length);
        return (table[word] & (1 << bit)) != 0;
    }

    private void WriteBit(byte area, int address, byte bitOffset, int offset, bool value)
    {
        var absoluteBit = address * 16 + bitOffset + offset;
        if (area == FinsMemoryAreaCode.TimerCounterFlag.Code)
        {
            EnsureBitRange(absoluteBit, _memory.TimerCounterFlags.Length);
            _memory.TimerCounterFlags[absoluteBit] = value;
            return;
        }

        var table = GetWordTable(area);
        var word = absoluteBit / 16;
        var bit = absoluteBit % 16;
        EnsureWordRange(word, 1, table.Length);
        if (value)
        {
            table[word] |= (ushort)(1 << bit);
        }
        else
        {
            table[word] &= (ushort)~(1 << bit);
        }
    }

    private ushort[] GetWordTable(byte area)
    {
        if (area is 0x30 or 0xB0) return _memory.CioWords;
        if (area is 0x31 or 0xB1) return _memory.WorkWords;
        if (area is 0x32 or 0xB2) return _memory.HoldingWords;
        if (area is 0x33 or 0xB3) return _memory.AuxiliaryWords;
        if (area is 0x02 or 0x82) return _memory.DataMemoryWords;
        if (area == 0x89) return _memory.TimerCounterValues;
        if (area is 0x0A or 0x98) return _memory.CurrentEmWords;
        if (area is >= 0x20 and <= 0x2F) return _memory.GetEmBank(area - 0x20);
        if (area is >= 0xA0 and <= 0xAF) return _memory.GetEmBank(area - 0xA0);
        if (area is >= 0xE0 and <= 0xE2) return _memory.GetEmBank(16 + area - 0xE0);
        if (area is >= 0x60 and <= 0x62) return _memory.GetEmBank(16 + area - 0x60);

        throw new FinsSlaveException(InvalidArea);
    }

    private static bool IsBitArea(byte area)
        => area is 0x30 or 0x31 or 0x32 or 0x33 or 0x02 or 0x09 or 0x0A
            || area is >= 0x20 and <= 0x2F
            || area is >= 0xE0 and <= 0xE2;

    private static void EnsureWordRange(int address, int count, int length)
    {
        if (address < 0 || count <= 0 || address + count > length)
        {
            throw new FinsSlaveException(AddressOutOfRange);
        }
    }

    private static void EnsureBitRange(int absoluteBit, int length)
    {
        if (absoluteBit < 0 || absoluteBit >= length)
        {
            throw new FinsSlaveException(AddressOutOfRange);
        }
    }

    private static FinsOptions CopyOptions(FinsOptions source)
        => new()
        {
            DestinationNetwork = source.DestinationNetwork,
            DestinationNode = source.DestinationNode,
            DestinationUnit = source.DestinationUnit,
            SourceNetwork = source.SourceNetwork,
            SourceNode = source.SourceNode,
            SourceUnit = source.SourceUnit,
            GatewayCount = source.GatewayCount,
            InformationControlField = source.InformationControlField,
            TcpRequestedClientNode = source.TcpRequestedClientNode,
            UseTcpNodeAddressHandshake = source.UseTcpNodeAddressHandshake,
            WordOrder = source.WordOrder
        };
}

internal sealed class FinsSlaveException : Exception
{
    public FinsSlaveException(ushort endCode) => EndCode = endCode;

    public ushort EndCode { get; }
}
