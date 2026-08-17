namespace Zeus;

/// <summary>
/// 虚拟 Modbus 从站。实现 <see cref="IVirtualResponder"/>，可直接交给 <c>AddVirtualChannel</c>。
/// </summary>
public sealed class ModbusSlaveResponder : IVirtualResponder
{
    private readonly byte _unitId;
    private readonly ModbusTransport _transport;
    private readonly ModbusSlaveMemory _memory;

    /// <summary>
    /// 创建从站应答器。
    /// </summary>
    /// <param name="unitId">本从站地址。不匹配的请求将被忽略。</param>
    /// <param name="transport">与主机相同的封装。</param>
    /// <param name="memory">寄存器映像。为 <c>null</c> 时使用默认容量。</param>
    public ModbusSlaveResponder(byte unitId, ModbusTransport transport, ModbusSlaveMemory? memory = null)
    {
        _unitId = unitId;
        _transport = transport;
        _memory = memory ?? new ModbusSlaveMemory();
    }

    /// <summary>可在测试中预置或断言的映像。</summary>
    public ModbusSlaveMemory Memory => _memory;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Respond(ReadOnlyMemory<byte> request)
    {
        if (!ModbusCodec.TryDecodeRequest(_transport, request.Span, out var unitId, out var pdu, out var transactionId))
        {
            return null;
        }

        if (unitId != _unitId)
        {
            return null;
        }

        byte[] responsePdu;
        try
        {
            responsePdu = Handle(pdu);
        }
        catch (ModbusException ex)
        {
            responsePdu = [(byte)(ex.Function | 0x80), (byte)ex.Code];
        }

        return ModbusCodec.EncodeRequest(_transport, _unitId, responsePdu, transactionId);
    }

    private byte[] Handle(byte[] pdu)
    {
        if (pdu.Length < 1)
        {
            throw new ModbusException(_unitId, 0, ModbusExceptionCode.IllegalFunction);
        }

        return pdu[0] switch
        {
            ModbusFunction.ReadCoils => ReadBits(pdu, _memory.Coils),
            ModbusFunction.ReadDiscreteInputs => ReadBits(pdu, _memory.DiscreteInputs),
            ModbusFunction.ReadHoldingRegisters => ReadRegisters(pdu, _memory.HoldingRegisters),
            ModbusFunction.ReadInputRegisters => ReadRegisters(pdu, _memory.InputRegisters),
            ModbusFunction.WriteSingleCoil => WriteSingleCoil(pdu),
            ModbusFunction.WriteSingleRegister => WriteSingleRegister(pdu),
            ModbusFunction.ReadExceptionStatus => ReadExceptionStatus(pdu),
            ModbusFunction.Diagnostics => Diagnostics(pdu),
            ModbusFunction.WriteMultipleCoils => WriteMultipleCoils(pdu),
            ModbusFunction.WriteMultipleRegisters => WriteMultipleRegisters(pdu),
            ModbusFunction.ReportServerId => ReportServerId(pdu),
            ModbusFunction.MaskWriteRegister => MaskWriteRegister(pdu),
            ModbusFunction.ReadWriteMultipleRegisters => ReadWriteMultipleRegisters(pdu),
            _ => throw new ModbusException(_unitId, pdu[0], ModbusExceptionCode.IllegalFunction)
        };
    }

    private byte[] ReadBits(byte[] pdu, bool[] table)
    {
        var (address, quantity) = ReadAddressQuantity(pdu);
        EnsureRange(address, quantity, table.Length, pdu[0]);
        var byteCount = ModbusCodec.CoilByteCount(quantity);
        var response = new byte[2 + byteCount];
        response[0] = pdu[0];
        response[1] = (byte)byteCount;
        for (var i = 0; i < quantity; i++)
        {
            if (table[address + i])
            {
                response[2 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        return response;
    }

    private byte[] ReadRegisters(byte[] pdu, ushort[] table)
    {
        var (address, quantity) = ReadAddressQuantity(pdu);
        EnsureRange(address, quantity, table.Length, pdu[0]);
        var response = new byte[2 + (quantity * 2)];
        response[0] = pdu[0];
        response[1] = (byte)(quantity * 2);
        for (var i = 0; i < quantity; i++)
        {
            ModbusCodec.WriteUInt16BigEndian(response.AsSpan(2 + (i * 2), 2), table[address + i]);
        }

        return response;
    }

    private byte[] WriteSingleCoil(byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            throw new ModbusException(_unitId, ModbusFunction.WriteSingleCoil, ModbusExceptionCode.IllegalDataValue);
        }

        var address = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var raw = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        if (raw is not (0x0000 or 0xFF00))
        {
            throw new ModbusException(_unitId, ModbusFunction.WriteSingleCoil, ModbusExceptionCode.IllegalDataValue);
        }

        EnsureRange(address, 1, _memory.Coils.Length, ModbusFunction.WriteSingleCoil);
        _memory.Coils[address] = raw == 0xFF00;
        return pdu.ToArray();
    }

    private byte[] WriteMultipleCoils(byte[] pdu)
    {
        if (pdu.Length < 6)
        {
            throw new ModbusException(_unitId, ModbusFunction.WriteMultipleCoils, ModbusExceptionCode.IllegalDataValue);
        }

        var address = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var quantity = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        var byteCount = pdu[5];
        if (byteCount != ModbusCodec.CoilByteCount(quantity) || pdu.Length < 6 + byteCount)
        {
            throw new ModbusException(_unitId, ModbusFunction.WriteMultipleCoils, ModbusExceptionCode.IllegalDataValue);
        }

        EnsureRange(address, quantity, _memory.Coils.Length, ModbusFunction.WriteMultipleCoils);
        for (var i = 0; i < quantity; i++)
        {
            _memory.Coils[address + i] = (pdu[6 + (i / 8)] & (1 << (i % 8))) != 0;
        }

        var response = new byte[5];
        response[0] = ModbusFunction.WriteMultipleCoils;
        ModbusCodec.WriteUInt16BigEndian(response.AsSpan(1, 2), address);
        ModbusCodec.WriteUInt16BigEndian(response.AsSpan(3, 2), quantity);
        return response;
    }

    private byte[] WriteSingleRegister(byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            throw new ModbusException(_unitId, ModbusFunction.WriteSingleRegister, ModbusExceptionCode.IllegalDataValue);
        }

        var address = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var value = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        EnsureRange(address, 1, _memory.HoldingRegisters.Length, ModbusFunction.WriteSingleRegister);
        _memory.HoldingRegisters[address] = value;
        return pdu.ToArray();
    }

    private byte[] ReadExceptionStatus(byte[] pdu)
    {
        if (pdu.Length != 1)
        {
            throw new ModbusException(_unitId, ModbusFunction.ReadExceptionStatus, ModbusExceptionCode.IllegalDataValue);
        }

        return [ModbusFunction.ReadExceptionStatus, _memory.ExceptionStatus];
    }

    private byte[] Diagnostics(byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            throw new ModbusException(_unitId, ModbusFunction.Diagnostics, ModbusExceptionCode.IllegalDataValue);
        }

        var subFunction = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        if (subFunction != 0x0000)
        {
            throw new ModbusException(_unitId, ModbusFunction.Diagnostics, ModbusExceptionCode.IllegalDataValue);
        }

        return pdu.ToArray();
    }

    private byte[] ReportServerId(byte[] pdu)
    {
        if (pdu.Length != 1)
        {
            throw new ModbusException(_unitId, ModbusFunction.ReportServerId, ModbusExceptionCode.IllegalDataValue);
        }

        var additionalData = _memory.ServerIdAdditionalData;
        var byteCount = 2 + additionalData.Length;
        if (byteCount > 251)
        {
            throw new ModbusException(_unitId, ModbusFunction.ReportServerId, ModbusExceptionCode.IllegalDataValue);
        }

        var response = new byte[2 + byteCount];
        response[0] = ModbusFunction.ReportServerId;
        response[1] = (byte)byteCount;
        response[2] = _memory.ServerId;
        response[3] = (byte)(_memory.ServerRunIndicatorStatus ? 0xFF : 0x00);
        additionalData.CopyTo(response.AsSpan(4));
        return response;
    }

    private byte[] WriteMultipleRegisters(byte[] pdu)
    {
        if (pdu.Length < 6)
        {
            throw new ModbusException(_unitId, ModbusFunction.WriteMultipleRegisters, ModbusExceptionCode.IllegalDataValue);
        }

        var address = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var quantity = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        var byteCount = pdu[5];
        if (byteCount != quantity * 2 || pdu.Length < 6 + byteCount)
        {
            throw new ModbusException(_unitId, ModbusFunction.WriteMultipleRegisters, ModbusExceptionCode.IllegalDataValue);
        }

        EnsureRange(address, quantity, _memory.HoldingRegisters.Length, ModbusFunction.WriteMultipleRegisters);
        for (var i = 0; i < quantity; i++)
        {
            _memory.HoldingRegisters[address + i] = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(6 + (i * 2), 2));
        }

        var response = new byte[5];
        response[0] = ModbusFunction.WriteMultipleRegisters;
        ModbusCodec.WriteUInt16BigEndian(response.AsSpan(1, 2), address);
        ModbusCodec.WriteUInt16BigEndian(response.AsSpan(3, 2), quantity);
        return response;
    }

    private byte[] MaskWriteRegister(byte[] pdu)
    {
        if (pdu.Length < 7)
        {
            throw new ModbusException(_unitId, ModbusFunction.MaskWriteRegister, ModbusExceptionCode.IllegalDataValue);
        }

        var address = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var andMask = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        var orMask = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(5, 2));
        EnsureRange(address, 1, _memory.HoldingRegisters.Length, ModbusFunction.MaskWriteRegister);

        var current = _memory.HoldingRegisters[address];
        _memory.HoldingRegisters[address] = (ushort)((current & andMask) | (orMask & ~andMask));
        return pdu.ToArray();
    }

    private byte[] ReadWriteMultipleRegisters(byte[] pdu)
    {
        if (pdu.Length < 10)
        {
            throw new ModbusException(_unitId, ModbusFunction.ReadWriteMultipleRegisters, ModbusExceptionCode.IllegalDataValue);
        }

        var readAddress = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var readQuantity = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        var writeAddress = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(5, 2));
        var writeQuantity = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(7, 2));
        var byteCount = pdu[9];
        if (readQuantity is < 1 or > 125
            || writeQuantity is < 1 or > 121
            || byteCount != writeQuantity * 2
            || pdu.Length < 10 + byteCount)
        {
            throw new ModbusException(_unitId, ModbusFunction.ReadWriteMultipleRegisters, ModbusExceptionCode.IllegalDataValue);
        }

        EnsureRange(writeAddress, writeQuantity, _memory.HoldingRegisters.Length, ModbusFunction.ReadWriteMultipleRegisters);
        EnsureRange(readAddress, readQuantity, _memory.HoldingRegisters.Length, ModbusFunction.ReadWriteMultipleRegisters);

        for (var i = 0; i < writeQuantity; i++)
        {
            _memory.HoldingRegisters[writeAddress + i] = ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(10 + (i * 2), 2));
        }

        var response = new byte[2 + (readQuantity * 2)];
        response[0] = ModbusFunction.ReadWriteMultipleRegisters;
        response[1] = (byte)(readQuantity * 2);
        for (var i = 0; i < readQuantity; i++)
        {
            ModbusCodec.WriteUInt16BigEndian(response.AsSpan(2 + (i * 2), 2), _memory.HoldingRegisters[readAddress + i]);
        }

        return response;
    }

    private (ushort Address, ushort Quantity) ReadAddressQuantity(byte[] pdu)
    {
        if (pdu.Length < 5)
        {
            throw new ModbusException(_unitId, pdu[0], ModbusExceptionCode.IllegalDataValue);
        }

        return (ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(1, 2)), ModbusCodec.ReadUInt16BigEndian(pdu.AsSpan(3, 2)));
    }

    private void EnsureRange(int address, int quantity, int length, byte function)
    {
        if (address < 0 || quantity <= 0 || address + quantity > length)
        {
            throw new ModbusException(_unitId, function, ModbusExceptionCode.IllegalDataAddress);
        }
    }
}
