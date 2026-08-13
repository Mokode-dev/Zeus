namespace Zeus;

/// <summary>
/// 帧校验计算。CRC-16 与 Modbus RTU 使用同一多项式，便于对照抓包。
/// </summary>
public static class FrameChecksum
{
    /// <summary>
    /// 计算校验字节。
    /// </summary>
    /// <param name="kind">算法。</param>
    /// <param name="data">参与校验的数据（长度域 + 载荷）。</param>
    /// <returns>校验字节；无校验时为空数组。</returns>
    public static byte[] Compute(FrameChecksumKind kind, ReadOnlySpan<byte> data)
    {
        return kind switch
        {
            FrameChecksumKind.None => [],
            FrameChecksumKind.Xor8 => [(byte)Xor(data)],
            FrameChecksumKind.Sum8 => [(byte)Sum(data)],
            FrameChecksumKind.Crc16Modbus => Crc16Bytes(data),
            _ => throw new ZeusProtocolException($"不支持的帧校验类型 {kind}。")
        };
    }

    /// <summary>
    /// 按 Modbus 多项式计算 CRC-16，返回主机端序的数值。
    /// </summary>
    /// <param name="data">参与计算的字节。</param>
    public static ushort Crc16Modbus(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var lsb = (crc & 0x0001) != 0;
                crc >>= 1;
                if (lsb)
                {
                    crc ^= 0xA001;
                }
            }
        }

        return crc;
    }

    private static int Xor(ReadOnlySpan<byte> data)
    {
        var acc = 0;
        foreach (var value in data)
        {
            acc ^= value;
        }

        return acc;
    }

    private static int Sum(ReadOnlySpan<byte> data)
    {
        var acc = 0;
        foreach (var value in data)
        {
            acc += value;
        }

        return acc;
    }

    private static byte[] Crc16Bytes(ReadOnlySpan<byte> data)
    {
        var crc = Crc16Modbus(data);
        return [(byte)(crc & 0xFF), (byte)(crc >> 8)];
    }
}
