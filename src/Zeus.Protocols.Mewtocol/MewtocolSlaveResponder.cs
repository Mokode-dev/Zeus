namespace Zeus;

/// <summary>
/// Panasonic MEWTOCOL-COM 虚拟 PLC。实现 <see cref="IVirtualResponder"/>，可直接交给 <c>AddVirtualChannel</c>。
/// </summary>
public sealed class MewtocolSlaveResponder : IVirtualResponder
{
    private const byte UnsupportedCommand = 0x20;
    private const byte BadFormat = 0x25;
    private const byte AddressOutOfRange = 0x26;
    private const byte BadData = 0x27;
    private readonly byte _stationNumber;
    private readonly MewtocolSlaveMemory _memory;

    /// <summary>创建虚拟 PLC。</summary>
    public MewtocolSlaveResponder(byte stationNumber = 1, MewtocolSlaveMemory? memory = null)
    {
        if (stationNumber is < 1 or > 99)
        {
            throw new ZeusException($"MEWTOCOL 站号必须介于 1 与 99 之间，当前为 {stationNumber}。");
        }

        _stationNumber = stationNumber;
        _memory = memory ?? new MewtocolSlaveMemory();
    }

    /// <summary>可在测试中预置或断言的映像。</summary>
    public MewtocolSlaveMemory Memory => _memory;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Respond(ReadOnlyMemory<byte> request)
    {
        if (!MewtocolCodec.TryDecodeRequestFrame(request.Span, out var frame))
        {
            return null;
        }

        if (frame.StationNumber != _stationNumber)
        {
            return null;
        }

        try
        {
            var responseText = Handle(frame.Command, frame.Text);
            return MewtocolCodec.EncodeResponse(_stationNumber, frame.Command, responseText);
        }
        catch (MewtocolSlaveException ex)
        {
            return MewtocolCodec.EncodeErrorResponse(_stationNumber, ex.ErrorCode);
        }
        catch (Exception)
        {
            return MewtocolCodec.EncodeErrorResponse(_stationNumber, BadFormat);
        }
    }

    private string Handle(string command, string text)
        => command switch
        {
            MewtocolCodec.ReadData => ReadDataWords(text),
            MewtocolCodec.WriteData => WriteDataWords(text),
            MewtocolCodec.ReadContact => ReadContactWords(text),
            MewtocolCodec.WriteContact => WriteContactWords(text),
            _ => throw new MewtocolSlaveException(UnsupportedCommand)
        };

    private string ReadDataWords(string text)
    {
        var (area, address, count) = MewtocolCodec.DecodeDataAddressRange(text);
        var table = GetDataTable(area);
        EnsureRange(address, count, table.Length);
        var values = new ushort[count];
        Array.Copy(table, address, values, 0, count);
        return MewtocolCodec.EncodeWords(values);
    }

    private string WriteDataWords(string text)
    {
        var (area, address, count) = MewtocolCodec.DecodeDataAddressRange(text);
        var values = MewtocolCodec.DecodeDataWriteWords(text, count);
        if (values.Length != count)
        {
            throw new MewtocolSlaveException(BadData);
        }

        var table = GetDataTable(area);
        EnsureRange(address, count, table.Length);
        Array.Copy(values, 0, table, address, count);
        return string.Empty;
    }

    private string ReadContactWords(string text)
    {
        var (area, wordAddress, count) = MewtocolCodec.DecodeContactAddressRange(text);
        var table = GetContactTable(area);
        EnsureRange(wordAddress, count, table.Length);
        var values = new ushort[count];
        Array.Copy(table, wordAddress, values, 0, count);
        return MewtocolCodec.EncodeWords(values);
    }

    private string WriteContactWords(string text)
    {
        var (area, wordAddress, count) = MewtocolCodec.DecodeContactAddressRange(text);
        if (area == MewtocolContactArea.ExternalInput)
        {
            throw new MewtocolSlaveException(AddressOutOfRange);
        }

        var values = MewtocolCodec.DecodeContactWriteWords(text, count);
        if (values.Length != count)
        {
            throw new MewtocolSlaveException(BadData);
        }

        var table = GetContactTable(area);
        EnsureRange(wordAddress, count, table.Length);
        Array.Copy(values, 0, table, wordAddress, count);
        return string.Empty;
    }

    private ushort[] GetDataTable(MewtocolDataArea area)
        => area switch
        {
            MewtocolDataArea.DataRegister => _memory.DataRegisterWords,
            MewtocolDataArea.LinkDataRegister => _memory.LinkDataRegisterWords,
            MewtocolDataArea.FileRegister => _memory.FileRegisterWords,
            _ => throw new MewtocolSlaveException(UnsupportedCommand)
        };

    private ushort[] GetContactTable(MewtocolContactArea area)
        => area switch
        {
            MewtocolContactArea.ExternalInput => _memory.ExternalInputWords,
            MewtocolContactArea.ExternalOutput => _memory.ExternalOutputWords,
            MewtocolContactArea.InternalRelay => _memory.InternalRelayWords,
            MewtocolContactArea.LinkRelay => _memory.LinkRelayWords,
            _ => throw new MewtocolSlaveException(UnsupportedCommand)
        };

    private static void EnsureRange(int address, int count, int length)
    {
        if (address < 0 || count <= 0 || address + count > length)
        {
            throw new MewtocolSlaveException(AddressOutOfRange);
        }
    }
}

internal sealed class MewtocolSlaveException : Exception
{
    public MewtocolSlaveException(byte errorCode) => ErrorCode = errorCode;

    public byte ErrorCode { get; }
}
