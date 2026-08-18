using System.Globalization;
using System.Text;

namespace Zeus;

/// <summary>
/// Omron Host Link ASCII 帧编解码与常用字区命令载荷构造。
/// </summary>
internal static class HostLinkCodec
{
    public const string ReadCio = "RR";
    public const string WriteCio = "WR";
    public const string ReadLink = "RL";
    public const string WriteLink = "WL";
    public const string ReadHolding = "RH";
    public const string WriteHolding = "WH";
    public const string ReadAuxiliary = "RJ";
    public const string WriteAuxiliary = "WJ";
    public const string ReadDataMemory = "RD";
    public const string WriteDataMemory = "WD";

    public static byte[] EncodeRequest(byte unitNumber, string command, string text)
    {
        ValidateUnitNumber(unitNumber);
        if (command.Length != 2)
        {
            throw new ZeusProtocolException("Host Link 命令头码必须是两个 ASCII 字符。");
        }

        var body = $"@{unitNumber:00}{command.ToUpperInvariant()}{text}";
        var fcs = ComputeFcs(body);
        return Encoding.ASCII.GetBytes($"{body}{fcs:X2}*\r");
    }

    public static bool TryDecodeResponse(
        IReadOnlyList<byte> buffer,
        out HostLinkResponseFrame response,
        out int consumed)
    {
        response = default;
        consumed = 0;
        var end = IndexOfCarriageReturn(buffer);
        if (end < 0)
        {
            return false;
        }

        consumed = end + 1;
        var frame = Copy(buffer, 0, consumed);
        if (!TryDecodeFrame(frame, out var unit, out var command, out var text))
        {
            throw new ZeusProtocolException("Host Link 响应帧格式或 FCS 校验失败。请确认通道连接的是 Host Link 设备。");
        }

        if (text.Length < 2 || !byte.TryParse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var endCode))
        {
            throw new ZeusProtocolException("Host Link 响应缺少两位十六进制结束码。");
        }

        response = new HostLinkResponseFrame(unit, command, endCode, text[2..]);
        return true;
    }

    public static bool TryDecodeRequestFrame(
        ReadOnlySpan<byte> frame,
        out HostLinkRequestFrame request)
    {
        request = default;
        if (!TryDecodeFrame(frame, out var unit, out var command, out var text))
        {
            return false;
        }

        request = new HostLinkRequestFrame(unit, command, text);
        return true;
    }

    public static byte[] EncodeResponse(byte unitNumber, string command, byte endCode, string text)
        => EncodeRequest(unitNumber, command, $"{endCode:X2}{text}");

    public static string BuildReadWordsText(ushort address, ushort count)
    {
        EnsureAddress(address);
        EnsureCount(count);
        return $"{address:0000}{count:0000}";
    }

    public static string BuildWriteWordsText(ushort address, IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        EnsureAddress(address);
        EnsureCount(values.Count);
        if (address + values.Count > 10000)
        {
            throw new ZeusProtocolException("Host Link 起始地址加数量不能超过 9999。");
        }

        var builder = new StringBuilder($"{address:0000}{values.Count:0000}");
        foreach (var value in values)
        {
            builder.Append(value.ToString("X4", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static (ushort Address, ushort Count) DecodeAddressAndCount(string text)
    {
        if (text.Length < 8
            || !ushort.TryParse(text[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var address)
            || !ushort.TryParse(text.Substring(4, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count == 0)
        {
            throw new ZeusProtocolException("Host Link 请求地址和数量必须是 4 位十进制地址 + 4 位十进制数量。");
        }

        EnsureAddress(address);
        EnsureCount(count);
        return (address, count);
    }

    public static ushort[] DecodeWriteWords(string text, ushort count)
    {
        if (text.Length < 8 + count * 4)
        {
            throw new ZeusProtocolException("Host Link 写字请求数据长度不足。");
        }

        var values = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ParseHexWord(text.Substring(8 + i * 4, 4));
        }

        return values;
    }

    public static ushort[] DecodeWordRead(string text, int count)
    {
        if (text.Length < count * 4)
        {
            throw new ZeusProtocolException("Host Link 读字响应长度不足。");
        }

        var values = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ParseHexWord(text.Substring(i * 4, 4));
        }

        return values;
    }

    public static string EncodeWords(IReadOnlyList<ushort> values)
    {
        var builder = new StringBuilder(values.Count * 4);
        foreach (var value in values)
        {
            builder.Append(value.ToString("X4", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static string ReadCommand(HostLinkArea area)
        => area switch
        {
            HostLinkArea.Cio => ReadCio,
            HostLinkArea.Link => ReadLink,
            HostLinkArea.Holding => ReadHolding,
            HostLinkArea.Auxiliary => ReadAuxiliary,
            HostLinkArea.DataMemory => ReadDataMemory,
            _ => throw new ZeusProtocolException($"不支持的 Host Link 区域：{area}。")
        };

    public static string WriteCommand(HostLinkArea area)
        => area switch
        {
            HostLinkArea.Cio => WriteCio,
            HostLinkArea.Link => WriteLink,
            HostLinkArea.Holding => WriteHolding,
            HostLinkArea.Auxiliary => WriteAuxiliary,
            HostLinkArea.DataMemory => WriteDataMemory,
            _ => throw new ZeusProtocolException($"不支持的 Host Link 区域：{area}。")
        };

    public static HostLinkArea AreaFromReadOrWriteCommand(string command)
        => command switch
        {
            ReadCio or WriteCio => HostLinkArea.Cio,
            ReadLink or WriteLink => HostLinkArea.Link,
            ReadHolding or WriteHolding => HostLinkArea.Holding,
            ReadAuxiliary or WriteAuxiliary => HostLinkArea.Auxiliary,
            ReadDataMemory or WriteDataMemory => HostLinkArea.DataMemory,
            _ => throw new ZeusProtocolException($"不支持的 Host Link 命令：{command}。")
        };

    public static bool IsWriteCommand(string command)
        => command is WriteCio or WriteLink or WriteHolding or WriteAuxiliary or WriteDataMemory;

    public static int GetWordCount(HostLinkDataType dataType)
        => dataType switch
        {
            HostLinkDataType.Bit => 1,
            HostLinkDataType.Word or HostLinkDataType.Int16 => 1,
            HostLinkDataType.UInt32 or HostLinkDataType.Int32 or HostLinkDataType.Real => 2,
            _ => throw new ZeusProtocolException($"不支持的 Host Link 数据类型：{dataType}。")
        };

    public static object DecodeValue(HostLinkDataType dataType, IReadOnlyList<ushort> words, byte bitOffset, double? scale, HostLinkWordOrder wordOrder)
    {
        if (words.Count < GetWordCount(dataType))
        {
            throw new ZeusProtocolException($"Host Link {dataType} 解码需要 {GetWordCount(dataType)} 个字。");
        }

        object raw = dataType switch
        {
            HostLinkDataType.Bit => (words[0] & (1 << bitOffset)) != 0,
            HostLinkDataType.Word => words[0],
            HostLinkDataType.Int16 => unchecked((short)words[0]),
            HostLinkDataType.UInt32 => CombineUInt32(words[0], words[1], wordOrder),
            HostLinkDataType.Int32 => unchecked((int)CombineUInt32(words[0], words[1], wordOrder)),
            HostLinkDataType.Real => BitConverter.Int32BitsToSingle(unchecked((int)CombineUInt32(words[0], words[1], wordOrder))),
            _ => throw new ZeusProtocolException($"不支持的 Host Link 数据类型：{dataType}。")
        };

        if (scale is null || raw is bool)
        {
            return raw;
        }

        var number = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        return number * scale.Value;
    }

    public static ushort[] EncodeValue(HostLinkDataType dataType, object value, double? scale, HostLinkWordOrder wordOrder)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (dataType == HostLinkDataType.Bit)
        {
            throw new ZeusProtocolException("Host Link Bit 值不能编码为完整字数组，请使用读改写流程。");
        }

        var actual = scale is { } factor
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture) / factor
            : value;

        return dataType switch
        {
            HostLinkDataType.Word => [ConvertToUInt16(actual, dataType)],
            HostLinkDataType.Int16 => [unchecked((ushort)ConvertToInt16(actual, dataType))],
            HostLinkDataType.UInt32 => SplitUInt32(ConvertToUInt32(actual, dataType), wordOrder),
            HostLinkDataType.Int32 => SplitUInt32(unchecked((uint)ConvertToInt32(actual, dataType)), wordOrder),
            HostLinkDataType.Real => SplitUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(ConvertToSingle(actual, dataType))), wordOrder),
            _ => throw new ZeusProtocolException($"不支持的 Host Link 数据类型：{dataType}。")
        };
    }

    private static bool TryDecodeFrame(ReadOnlySpan<byte> frame, out byte unit, out string command, out string text)
    {
        unit = 0;
        command = string.Empty;
        text = string.Empty;
        if (frame.Length < 8 || frame[0] != (byte)'@' || frame[^2] != (byte)'*' || frame[^1] != (byte)'\r')
        {
            return false;
        }

        var ascii = Encoding.ASCII.GetString(frame);
        if (!byte.TryParse(ascii.Substring(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out unit))
        {
            return false;
        }

        var fcsStart = ascii.Length - 4;
        if (!byte.TryParse(ascii.Substring(fcsStart, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var expectedFcs))
        {
            return false;
        }

        var body = ascii[..fcsStart];
        if (ComputeFcs(body) != expectedFcs)
        {
            return false;
        }

        command = ascii.Substring(3, 2);
        text = ascii.Substring(5, fcsStart - 5);
        return true;
    }

    private static byte ComputeFcs(string body)
    {
        byte fcs = 0;
        foreach (var value in Encoding.ASCII.GetBytes(body))
        {
            fcs ^= value;
        }

        return fcs;
    }

    private static int IndexOfCarriageReturn(IReadOnlyList<byte> buffer)
    {
        for (var i = 0; i < buffer.Count; i++)
        {
            if (buffer[i] == (byte)'\r')
            {
                return i;
            }
        }

        return -1;
    }

    private static ushort ParseHexWord(string text)
    {
        if (!ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            throw new ZeusProtocolException($"Host Link 字数据「{text}」不是 4 位十六进制数。");
        }

        return value;
    }

    private static void ValidateUnitNumber(byte unitNumber)
    {
        if (unitNumber > 31)
        {
            throw new ZeusProtocolException($"Host Link 单元号必须介于 0 与 31 之间，当前为 {unitNumber}。");
        }
    }

    private static void EnsureAddress(ushort address)
    {
        if (address > 9999)
        {
            throw new ZeusProtocolException($"Host Link 地址必须介于 0 与 9999 之间，当前为 {address}。");
        }
    }

    private static void EnsureCount(int count)
    {
        if (count is < 1 or > 9999)
        {
            throw new ZeusProtocolException($"Host Link 读取/写入数量必须在 1 到 9999 之间，当前为 {count}。");
        }
    }

    private static uint CombineUInt32(ushort first, ushort second, HostLinkWordOrder wordOrder)
        => wordOrder == HostLinkWordOrder.HighWordFirst
            ? ((uint)first << 16) | second
            : ((uint)second << 16) | first;

    private static ushort[] SplitUInt32(uint value, HostLinkWordOrder wordOrder)
    {
        var high = (ushort)(value >> 16);
        var low = (ushort)(value & 0xFFFF);
        return wordOrder == HostLinkWordOrder.HighWordFirst ? [high, low] : [low, high];
    }

    private static ushort ConvertToUInt16(object value, HostLinkDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < ushort.MinValue or > ushort.MaxValue)
        {
            throw new ZeusProtocolException($"Host Link {dataType} 写入值 {value} 超出 UInt16 范围。");
        }

        return (ushort)number;
    }

    private static short ConvertToInt16(object value, HostLinkDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < short.MinValue or > short.MaxValue)
        {
            throw new ZeusProtocolException($"Host Link {dataType} 写入值 {value} 超出 Int16 范围。");
        }

        return (short)number;
    }

    private static uint ConvertToUInt32(object value, HostLinkDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < uint.MinValue or > uint.MaxValue)
        {
            throw new ZeusProtocolException($"Host Link {dataType} 写入值 {value} 超出 UInt32 范围。");
        }

        return (uint)number;
    }

    private static int ConvertToInt32(object value, HostLinkDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < int.MinValue or > int.MaxValue)
        {
            throw new ZeusProtocolException($"Host Link {dataType} 写入值 {value} 超出 Int32 范围。");
        }

        return (int)number;
    }

    private static float ConvertToSingle(object value, HostLinkDataType dataType)
    {
        var number = Convert.ToSingle(value, CultureInfo.InvariantCulture);
        if (!float.IsFinite(number))
        {
            throw new ZeusProtocolException($"Host Link {dataType} 写入值必须是有限数值。");
        }

        return number;
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

internal readonly record struct HostLinkRequestFrame(byte UnitNumber, string Command, string Text);

internal readonly record struct HostLinkResponseFrame(byte UnitNumber, string Command, byte EndCode, string Text);
