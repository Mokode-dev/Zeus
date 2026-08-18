namespace Zeus;

/// <summary>
/// Omron Host Link 虚拟 PLC。实现 <see cref="IVirtualResponder"/>，可直接交给 <c>AddVirtualChannel</c>。
/// </summary>
public sealed class HostLinkSlaveResponder : IVirtualResponder
{
    private const byte Success = 0x00;
    private const byte AddressOutOfRange = 0x04;
    private const byte BadFormat = 0x14;
    private const byte BadData = 0x15;
    private const byte UnsupportedCommand = 0x16;
    private readonly byte _unitNumber;
    private readonly HostLinkSlaveMemory _memory;

    /// <summary>创建虚拟 PLC。</summary>
    public HostLinkSlaveResponder(byte unitNumber = 0, HostLinkSlaveMemory? memory = null)
    {
        if (unitNumber > 31)
        {
            throw new ZeusException($"Host Link 单元号必须介于 0 与 31 之间，当前为 {unitNumber}。");
        }

        _unitNumber = unitNumber;
        _memory = memory ?? new HostLinkSlaveMemory();
    }

    /// <summary>可在测试中预置或断言的映像。</summary>
    public HostLinkSlaveMemory Memory => _memory;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Respond(ReadOnlyMemory<byte> request)
    {
        if (!HostLinkCodec.TryDecodeRequestFrame(request.Span, out var frame))
        {
            return null;
        }

        if (frame.UnitNumber != _unitNumber)
        {
            return null;
        }

        try
        {
            var responseText = Handle(frame.Command, frame.Text);
            return HostLinkCodec.EncodeResponse(_unitNumber, frame.Command, Success, responseText);
        }
        catch (HostLinkSlaveException ex)
        {
            return HostLinkCodec.EncodeResponse(_unitNumber, frame.Command, ex.EndCode, string.Empty);
        }
        catch (Exception)
        {
            return HostLinkCodec.EncodeResponse(_unitNumber, frame.Command, BadFormat, string.Empty);
        }
    }

    private string Handle(string command, string text)
    {
        var area = HostLinkCodec.AreaFromReadOrWriteCommand(command);
        return HostLinkCodec.IsWriteCommand(command)
            ? WriteWords(area, text)
            : ReadWords(area, text);
    }

    private string ReadWords(HostLinkArea area, string text)
    {
        var (address, count) = HostLinkCodec.DecodeAddressAndCount(text);
        var table = GetTable(area);
        EnsureRange(address, count, table.Length);
        var values = new ushort[count];
        Array.Copy(table, address, values, 0, count);
        return HostLinkCodec.EncodeWords(values);
    }

    private string WriteWords(HostLinkArea area, string text)
    {
        var (address, count) = HostLinkCodec.DecodeAddressAndCount(text);
        var values = HostLinkCodec.DecodeWriteWords(text, count);
        if (values.Length != count)
        {
            throw new HostLinkSlaveException(BadData);
        }

        var table = GetTable(area);
        EnsureRange(address, count, table.Length);
        Array.Copy(values, 0, table, address, count);
        return string.Empty;
    }

    private ushort[] GetTable(HostLinkArea area)
        => area switch
        {
            HostLinkArea.Cio => _memory.CioWords,
            HostLinkArea.Link => _memory.LinkWords,
            HostLinkArea.Holding => _memory.HoldingWords,
            HostLinkArea.Auxiliary => _memory.AuxiliaryWords,
            HostLinkArea.DataMemory => _memory.DataMemoryWords,
            _ => throw new HostLinkSlaveException(UnsupportedCommand)
        };

    private static void EnsureRange(int address, int count, int length)
    {
        if (address < 0 || count <= 0 || address + count > length)
        {
            throw new HostLinkSlaveException(AddressOutOfRange);
        }
    }
}

internal sealed class HostLinkSlaveException : Exception
{
    public HostLinkSlaveException(byte endCode) => EndCode = endCode;

    public byte EndCode { get; }
}
