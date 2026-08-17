namespace Zeus;

/// <summary>
/// Allen-Bradley EtherNet/IP 虚拟 PLC。实现 <see cref="IVirtualResponder"/>，可直接交给 <c>AddVirtualChannel</c>。
/// </summary>
public sealed class EtherNetIpSlaveResponder : IVirtualResponder
{
    private const byte Success = 0x00;
    private const byte PathSegmentError = 0x04;
    private const byte ServiceNotSupported = 0x08;
    private const byte AttributeNotSupported = 0x14;
    private readonly EtherNetIpSlaveMemory _memory;
    private uint _nextSession = 1;

    /// <summary>创建虚拟 PLC。</summary>
    public EtherNetIpSlaveResponder(EtherNetIpSlaveMemory? memory = null)
    {
        _memory = memory ?? CreateDefaultMemory();
    }

    /// <summary>可在测试中预置或断言的映像。</summary>
    public EtherNetIpSlaveMemory Memory => _memory;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Respond(ReadOnlyMemory<byte> request)
    {
        if (!EtherNetIpCodec.TryDecodePacket(request.ToArray(), out var packet, out _))
        {
            return null;
        }

        return packet.Command switch
        {
            EtherNetIpCodec.RegisterSession => Register(packet),
            EtherNetIpCodec.UnregisterSession => ReadOnlyMemory<byte>.Empty,
            EtherNetIpCodec.SendRRData => SendRRData(packet),
            _ => null
        };
    }

    private ReadOnlyMemory<byte> Register(EtherNetIpPacket packet)
    {
        var body = new byte[4];
        EtherNetIpCodec.WriteUInt16LittleEndian(body.AsSpan(0, 2), 1);
        EtherNetIpCodec.WriteUInt16LittleEndian(body.AsSpan(2, 2), 0);
        var session = _nextSession++;
        return EncodePacket(EtherNetIpCodec.RegisterSession, session, packet.SenderContext, body);
    }

    private ReadOnlyMemory<byte> SendRRData(EtherNetIpPacket packet)
    {
        byte[] cipResponse;
        try
        {
            var cip = EtherNetIpCodec.DecodeSendRRData(packet.Data);
            var decoded = EtherNetIpCodec.DecodeCipRequest(cip);
            cipResponse = Handle(decoded.Service, decoded.Path, decoded.Data);
        }
        catch (ZeusProtocolException)
        {
            cipResponse = EtherNetIpCodec.EncodeCipResponse(0, PathSegmentError, []);
        }
        catch (Exception)
        {
            cipResponse = EtherNetIpCodec.EncodeCipResponse(0, PathSegmentError, []);
        }

        return EtherNetIpCodec.EncodeSendRRDataResponse(packet.SessionHandle, packet.SenderContext, cipResponse);
    }

    private byte[] Handle(byte service, byte[] path, byte[] data)
        => service switch
        {
            EtherNetIpCodec.ServiceReadTag => ReadTag(service, path, data),
            EtherNetIpCodec.ServiceWriteTag => WriteTag(service, path, data),
            EtherNetIpCodec.ServiceGetAttributeSingle => GetAttribute(service, path),
            EtherNetIpCodec.ServiceSetAttributeSingle => SetAttribute(service, path, data),
            _ => EtherNetIpCodec.EncodeCipResponse(service, ServiceNotSupported, [])
        };

    private byte[] ReadTag(byte service, byte[] path, byte[] data)
    {
        if (data.Length < 2)
        {
            return EtherNetIpCodec.EncodeCipResponse(service, PathSegmentError, []);
        }

        var tagName = EtherNetIpCodec.DecodeSymbolPath(path);
        if (!_memory.TryGetTag(tagName, out var value))
        {
            return EtherNetIpCodec.EncodeCipResponse(service, PathSegmentError, []);
        }

        return EtherNetIpCodec.EncodeCipResponse(service, Success, EtherNetIpCodec.EncodeTagReadResponse(value.DataType, value.Value));
    }

    private byte[] WriteTag(byte service, byte[] path, byte[] data)
    {
        if (data.Length < 4)
        {
            return EtherNetIpCodec.EncodeCipResponse(service, PathSegmentError, []);
        }

        var tagName = EtherNetIpCodec.DecodeSymbolPath(path);
        var dataType = (EtherNetIpDataType)EtherNetIpCodec.ReadUInt16LittleEndian(data.AsSpan(0, 2));
        var elementCount = EtherNetIpCodec.ReadUInt16LittleEndian(data.AsSpan(2, 2));
        if (elementCount != 1)
        {
            return EtherNetIpCodec.EncodeCipResponse(service, ServiceNotSupported, []);
        }

        var value = EtherNetIpCodec.DecodeValue(dataType, data.AsSpan(4));
        _memory.SetTag(tagName, dataType, value);
        return EtherNetIpCodec.EncodeCipResponse(service, Success, []);
    }

    private byte[] GetAttribute(byte service, byte[] path)
    {
        var (classId, instanceId, attributeId) = EtherNetIpCodec.DecodeAttributePath(path);
        return _memory.TryGetAttribute(classId, instanceId, attributeId, out var value)
            ? EtherNetIpCodec.EncodeCipResponse(service, Success, value)
            : EtherNetIpCodec.EncodeCipResponse(service, AttributeNotSupported, []);
    }

    private byte[] SetAttribute(byte service, byte[] path, byte[] data)
    {
        var (classId, instanceId, attributeId) = EtherNetIpCodec.DecodeAttributePath(path);
        _memory.SetAttribute(classId, instanceId, attributeId, data);
        return EtherNetIpCodec.EncodeCipResponse(service, Success, []);
    }

    private static byte[] EncodePacket(ushort command, uint sessionHandle, ulong senderContext, ReadOnlySpan<byte> data)
    {
        var packet = new byte[24 + data.Length];
        EtherNetIpCodec.WriteUInt16LittleEndian(packet.AsSpan(0, 2), command);
        EtherNetIpCodec.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)data.Length);
        EtherNetIpCodec.WriteUInt32LittleEndian(packet.AsSpan(4, 4), sessionHandle);
        EtherNetIpCodec.WriteUInt32LittleEndian(packet.AsSpan(8, 4), 0);
        EtherNetIpCodec.WriteUInt64LittleEndian(packet.AsSpan(12, 8), senderContext);
        EtherNetIpCodec.WriteUInt32LittleEndian(packet.AsSpan(20, 4), 0);
        data.CopyTo(packet.AsSpan(24));
        return packet;
    }

    private static EtherNetIpSlaveMemory CreateDefaultMemory()
    {
        var memory = new EtherNetIpSlaveMemory();
        memory.SetTag("Temperature", EtherNetIpDataType.Int, (short)253);
        memory.SetTag("Running", EtherNetIpDataType.Bool, true);
        memory.SetTag("Program:Main.Speed", EtherNetIpDataType.DInt, 1450);
        return memory;
    }
}
