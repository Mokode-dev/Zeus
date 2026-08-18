namespace Zeus;

/// <summary>
/// DL/T 645-2007 虚拟表计。实现 <see cref="IVirtualResponder"/>，可直接交给 <c>AddVirtualChannel</c>。
/// </summary>
public sealed class Dlt645SlaveResponder : IVirtualResponder
{
    private const byte OtherError = 0x01;
    private const byte NoRequestedData = 0x02;
    private readonly string _meterAddress;
    private readonly Dlt645SlaveMemory _memory;

    /// <summary>创建虚拟表计。</summary>
    public Dlt645SlaveResponder(string meterAddress = "000000000001", Dlt645SlaveMemory? memory = null)
    {
        Dlt645Codec.ValidateAddress(meterAddress);
        _meterAddress = meterAddress.Trim();
        _memory = memory ?? CreateDefaultMemory();
    }

    /// <summary>可在测试中预置或断言的数据项映像。</summary>
    public Dlt645SlaveMemory Memory => _memory;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Respond(ReadOnlyMemory<byte> request)
    {
        if (!Dlt645Codec.TryDecodeFrame(request.ToArray(), out var frame, out _))
        {
            return null;
        }

        if (!string.Equals(frame.MeterAddress, _meterAddress, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return frame.ControlCode switch
            {
                Dlt645Codec.ReadData => ReadData(frame.Data),
                Dlt645Codec.WriteData => WriteData(frame.Data),
                _ => Dlt645Codec.EncodeResponse(_meterAddress, ErrorControlCode(frame.ControlCode), [OtherError])
            };
        }
        catch (Dlt645SlaveException ex)
        {
            return Dlt645Codec.EncodeResponse(_meterAddress, ErrorControlCode(frame.ControlCode), [ex.ErrorCode]);
        }
        catch (Exception)
        {
            return Dlt645Codec.EncodeResponse(_meterAddress, ErrorControlCode(frame.ControlCode), [OtherError]);
        }
    }

    private byte[] ReadData(IReadOnlyList<byte> data)
    {
        var dataIdentifier = Dlt645Codec.DecodeDataIdentifier(data);
        if (!_memory.TryGetData(dataIdentifier, out var value))
        {
            throw new Dlt645SlaveException(NoRequestedData);
        }

        var response = new byte[4 + value.Length];
        Array.Copy(Dlt645Codec.EncodeDataIdentifier(dataIdentifier), response, 4);
        Array.Copy(value, 0, response, 4, value.Length);
        return Dlt645Codec.EncodeResponse(_meterAddress, Dlt645Codec.ReadDataResponse, response);
    }

    private byte[] WriteData(IReadOnlyList<byte> data)
    {
        if (data.Count < 12)
        {
            throw new Dlt645SlaveException(OtherError);
        }

        var dataIdentifier = Dlt645Codec.DecodeDataIdentifier(data);
        var value = data.Skip(12).ToArray();
        if (value.Length == 0)
        {
            throw new Dlt645SlaveException(OtherError);
        }

        _memory.SetData(dataIdentifier, value);
        return Dlt645Codec.EncodeResponse(_meterAddress, Dlt645Codec.WriteDataResponse, []);
    }

    private static byte ErrorControlCode(byte requestControlCode)
        => (byte)(requestControlCode | 0xC0);

    private static Dlt645SlaveMemory CreateDefaultMemory()
    {
        var memory = new Dlt645SlaveMemory();
        memory.SetBcd(0x00000000, 1234.56, byteLength: 4, scale: 0.01);
        memory.SetBcd(0x02010100, 220.5, byteLength: 2, scale: 0.1);
        memory.SetBcd(0x02020100, 5.123, byteLength: 3, scale: 0.001);
        memory.SetBcd(0x04000101, 10.0, byteLength: 2, scale: 0.1);
        return memory;
    }
}

internal sealed class Dlt645SlaveException : Exception
{
    public Dlt645SlaveException(byte errorCode) => ErrorCode = errorCode;

    public byte ErrorCode { get; }
}
