namespace Zeus;

/// <summary>
/// Modbus RTU / TCP 的 PDU 封包与拆包。
/// RTU 响应长度依赖未完成请求；TCP 响应长度由 MBAP 给出。
/// </summary>
internal static class ModbusCodec
{
    /// <summary>
    /// 将 PDU 封装为可写入通道的完整 ADU。
    /// </summary>
    /// <param name="transport">封装类型。</param>
    /// <param name="unitId">从站/单元标识。</param>
    /// <param name="pdu">功能码 + 数据。</param>
    /// <param name="transactionId">仅 TCP 使用的事务号。</param>
    public static byte[] EncodeRequest(ModbusTransport transport, byte unitId, ReadOnlySpan<byte> pdu, ushort transactionId)
    {
        if (transport == ModbusTransport.Tcp)
        {
            return EncodeTcp(unitId, pdu, transactionId);
        }

        var adu = new byte[1 + pdu.Length + 2];
        adu[0] = unitId;
        pdu.CopyTo(adu.AsSpan(1));
        var crc = FrameChecksum.Crc16Modbus(adu.AsSpan(0, 1 + pdu.Length));
        adu[^2] = (byte)(crc & 0xFF);
        adu[^1] = (byte)(crc >> 8);
        return adu;
    }

    /// <summary>
    /// 估算 RTU 正常响应的最小长度（含地址与 CRC）。异常帧固定为 5 字节。
    /// </summary>
    /// <param name="requestPdu">已发送的请求 PDU。</param>
    public static int ExpectedRtuResponseLength(ReadOnlySpan<byte> requestPdu)
    {
        if (requestPdu.Length < 1)
        {
            return 5;
        }

        return requestPdu[0] switch
        {
            ModbusFunction.ReadCoils or ModbusFunction.ReadDiscreteInputs when requestPdu.Length >= 5 =>
                5 + CoilByteCount(ReadUInt16BigEndian(requestPdu.Slice(3, 2))),
            ModbusFunction.ReadHoldingRegisters or ModbusFunction.ReadInputRegisters when requestPdu.Length >= 5 =>
                5 + (ReadUInt16BigEndian(requestPdu.Slice(3, 2)) * 2),
            ModbusFunction.MaskWriteRegister => 10,
            _ => 8
        };
    }

    /// <summary>
    /// 从缓冲中尝试取出一帧完整响应。
    /// </summary>
    /// <param name="transport">封装类型。</param>
    /// <param name="buffer">累计接收缓冲。</param>
    /// <param name="requestPdu">对应的请求 PDU，用于推算 RTU 长度。</param>
    /// <param name="expectedTransactionId">TCP 事务号。</param>
    /// <param name="unitId">解析出的单元标识。</param>
    /// <param name="pdu">解析出的 PDU。</param>
    /// <param name="consumed">应从缓冲移除的字节数。</param>
    public static bool TryDecodeResponse(
        ModbusTransport transport,
        IReadOnlyList<byte> buffer,
        ReadOnlySpan<byte> requestPdu,
        ushort expectedTransactionId,
        out byte unitId,
        out byte[] pdu,
        out int consumed)
    {
        unitId = 0;
        pdu = [];
        consumed = 0;

        if (transport == ModbusTransport.Tcp)
        {
            return TryDecodeTcp(buffer, expectedTransactionId, out unitId, out pdu, out consumed);
        }

        if (buffer.Count < 5)
        {
            return false;
        }

        var isException = (buffer[1] & 0x80) != 0;
        var needed = isException ? 5 : ExpectedRtuResponseLength(requestPdu);
        if (buffer.Count < needed)
        {
            return false;
        }

        var frame = Copy(buffer, 0, needed);
        var crc = FrameChecksum.Crc16Modbus(frame.AsSpan(0, needed - 2));
        if (frame[^2] != (byte)(crc & 0xFF) || frame[^1] != (byte)(crc >> 8))
        {
            throw new ZeusProtocolException("Modbus RTU 响应 CRC 校验失败。请核对波特率、从站地址，或改用虚拟从站确认主机逻辑。");
        }

        unitId = frame[0];
        pdu = frame[1..^2];
        consumed = needed;
        return true;
    }

    /// <summary>
    /// 解析一帧完整请求（虚拟从站使用：一次写入即一整帧）。
    /// </summary>
    /// <param name="transport">封装类型。</param>
    /// <param name="adu">完整 ADU。</param>
    /// <param name="unitId">单元标识。</param>
    /// <param name="pdu">PDU。</param>
    /// <param name="transactionId">TCP 事务号；RTU 为 0。</param>
    public static bool TryDecodeRequest(
        ModbusTransport transport,
        ReadOnlySpan<byte> adu,
        out byte unitId,
        out byte[] pdu,
        out ushort transactionId)
    {
        unitId = 0;
        pdu = [];
        transactionId = 0;

        if (transport == ModbusTransport.Tcp)
        {
            if (adu.Length < 8)
            {
                return false;
            }

            transactionId = ReadUInt16BigEndian(adu);
            var protocol = ReadUInt16BigEndian(adu.Slice(2, 2));
            var length = ReadUInt16BigEndian(adu.Slice(4, 2));
            if (protocol != 0 || length < 2 || adu.Length < 6 + length)
            {
                return false;
            }

            unitId = adu[6];
            pdu = adu.Slice(7, length - 1).ToArray();
            return pdu.Length > 0;
        }

        if (adu.Length < 4)
        {
            return false;
        }

        var crc = FrameChecksum.Crc16Modbus(adu[..^2]);
        if (adu[^2] != (byte)(crc & 0xFF) || adu[^1] != (byte)(crc >> 8))
        {
            return false;
        }

        unitId = adu[0];
        pdu = adu[1..^2].ToArray();
        return pdu.Length > 0;
    }

    /// <summary>
    /// 读取大端 16 位无符号整数。
    /// </summary>
    public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> data)
        => (ushort)((data[0] << 8) | data[1]);

    /// <summary>
    /// 写入大端 16 位无符号整数。
    /// </summary>
    public static void WriteUInt16BigEndian(Span<byte> dest, ushort value)
    {
        dest[0] = (byte)(value >> 8);
        dest[1] = (byte)(value & 0xFF);
    }

    /// <summary>
    /// 线圈数量对应的字节数。
    /// </summary>
    public static int CoilByteCount(int quantity) => (quantity + 7) / 8;

    private static byte[] EncodeTcp(byte unitId, ReadOnlySpan<byte> pdu, ushort transactionId)
    {
        var length = 1 + pdu.Length;
        var adu = new byte[6 + length];
        WriteUInt16BigEndian(adu.AsSpan(0, 2), transactionId);
        WriteUInt16BigEndian(adu.AsSpan(2, 2), 0);
        WriteUInt16BigEndian(adu.AsSpan(4, 2), (ushort)length);
        adu[6] = unitId;
        pdu.CopyTo(adu.AsSpan(7));
        return adu;
    }

    private static bool TryDecodeTcp(
        IReadOnlyList<byte> buffer,
        ushort expectedTransactionId,
        out byte unitId,
        out byte[] pdu,
        out int consumed)
    {
        unitId = 0;
        pdu = [];
        consumed = 0;
        if (buffer.Count < 8)
        {
            return false;
        }

        var length = (buffer[4] << 8) | buffer[5];
        var total = 6 + length;
        if (length < 2 || buffer.Count < total)
        {
            return false;
        }

        var transaction = (ushort)((buffer[0] << 8) | buffer[1]);
        var protocol = (ushort)((buffer[2] << 8) | buffer[3]);
        if (protocol != 0)
        {
            throw new ZeusProtocolException($"Modbus TCP 协议标识为 {protocol}，期望 0。请确认对端是 Modbus TCP 而不是原始套接字。");
        }

        if (transaction != expectedTransactionId)
        {
            throw new ZeusProtocolException(
                $"Modbus TCP 事务号不匹配：收到 {transaction}，期望 {expectedTransactionId}。请避免多路并发共用同一通道。");
        }

        unitId = buffer[6];
        pdu = Copy(buffer, 7, length - 1);
        consumed = total;
        return true;
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
