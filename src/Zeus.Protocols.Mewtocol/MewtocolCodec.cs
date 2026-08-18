using System.Globalization;
using System.Text;

namespace Zeus;

/// <summary>
/// Panasonic MEWTOCOL-COM ASCII 帧编解码与常用字区命令载荷构造。
/// </summary>
internal static class MewtocolCodec
{
    public const string ReadData = "RD";
    public const string WriteData = "WD";
    public const string ReadContact = "RC";
    public const string WriteContact = "WC";

    public static byte[] EncodeRequest(byte stationNumber, string command, string text)
    {
        ValidateStationNumber(stationNumber);
        if (command.Length != 2)
        {
            throw new ZeusProtocolException("MEWTOCOL 命令必须是两个 ASCII 字符。");
        }

        var body = $"%{stationNumber:00}#{command.ToUpperInvariant()}{text}";
        var bcc = ComputeBcc(body);
        return Encoding.ASCII.GetBytes($"{body}{bcc:X2}\r");
    }

    public static bool TryDecodeResponse(
        IReadOnlyList<byte> buffer,
        out MewtocolResponseFrame response,
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
        if (!TryDecodeFrame(frame, out var station, out var marker, out var command, out var text))
        {
            throw new ZeusProtocolException("MEWTOCOL 响应帧格式或 BCC 校验失败。请确认通道连接的是 MEWTOCOL-COM 设备。");
        }

        if (marker == '$')
        {
            response = new MewtocolResponseFrame(station, command, 0, text);
            return true;
        }

        if (marker == '!'
            && byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var errorCode))
        {
            response = new MewtocolResponseFrame(station, string.Empty, errorCode, string.Empty);
            return true;
        }

        throw new ZeusProtocolException("MEWTOCOL 响应帧头标识必须是 $ 或 !。");
    }

    public static bool TryDecodeRequestFrame(
        ReadOnlySpan<byte> frame,
        out MewtocolRequestFrame request)
    {
        request = default;
        if (!TryDecodeFrame(frame, out var station, out var marker, out var command, out var text) || marker != '#')
        {
            return false;
        }

        request = new MewtocolRequestFrame(station, command, text);
        return true;
    }

    public static byte[] EncodeResponse(byte stationNumber, string command, string text)
    {
        ValidateStationNumber(stationNumber);
        if (command.Length != 2)
        {
            throw new ZeusProtocolException("MEWTOCOL 响应命令必须是两个 ASCII 字符。");
        }

        var body = $"%{stationNumber:00}${command.ToUpperInvariant()}{text}";
        return Encoding.ASCII.GetBytes($"{body}{ComputeBcc(body):X2}\r");
    }

    public static byte[] EncodeErrorResponse(byte stationNumber, byte errorCode)
    {
        ValidateStationNumber(stationNumber);
        var body = $"%{stationNumber:00}!{errorCode:X2}";
        return Encoding.ASCII.GetBytes($"{body}{ComputeBcc(body):X2}\r");
    }

    public static string BuildReadDataWordsText(MewtocolDataArea area, int address, int count)
    {
        EnsureDataAddress(address);
        EnsureCount(count);
        var end = checked(address + count - 1);
        EnsureDataAddress(end);
        return $"{DataAreaCode(area)}{address:00000}{end:00000}";
    }

    public static string BuildWriteDataWordsText(MewtocolDataArea area, int address, IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        EnsureCount(values.Count);
        var builder = new StringBuilder(BuildReadDataWordsText(area, address, values.Count));
        AppendWords(builder, values);
        return builder.ToString();
    }

    public static string BuildReadContactWordsText(MewtocolContactArea area, int wordAddress, int count)
    {
        EnsureContactWordAddress(wordAddress);
        EnsureCount(count);
        var end = checked(wordAddress + count - 1);
        EnsureContactWordAddress(end);
        return $"C{ContactAreaCode(area)}{wordAddress:0000}{end:0000}";
    }

    public static string BuildWriteContactWordsText(MewtocolContactArea area, int wordAddress, IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        EnsureCount(values.Count);
        var builder = new StringBuilder(BuildReadContactWordsText(area, wordAddress, values.Count));
        AppendWords(builder, values);
        return builder.ToString();
    }

    public static (MewtocolDataArea Area, int Address, int Count) DecodeDataAddressRange(string text)
    {
        if (text.Length < 11
            || !TryDataAreaFromCode(text[0], out var area)
            || !int.TryParse(text.Substring(1, 5), NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || !int.TryParse(text.Substring(6, 5), NumberStyles.None, CultureInfo.InvariantCulture, out var end)
            || end < start)
        {
            throw new ZeusProtocolException("MEWTOCOL RD/WD 请求必须是区域码 + 5 位起始字地址 + 5 位结束字地址。");
        }

        EnsureDataAddress(start);
        EnsureDataAddress(end);
        return (area, start, end - start + 1);
    }

    public static (MewtocolContactArea Area, int WordAddress, int Count) DecodeContactAddressRange(string text)
    {
        if (text.Length < 10
            || text[0] != 'C'
            || !TryContactAreaFromCode(text[1], out var area)
            || !int.TryParse(text.Substring(2, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || !int.TryParse(text.Substring(6, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var end)
            || end < start)
        {
            throw new ZeusProtocolException("MEWTOCOL RC/WC 请求必须是 C + 接点区码 + 4 位起始字地址 + 4 位结束字地址。");
        }

        EnsureContactWordAddress(start);
        EnsureContactWordAddress(end);
        return (area, start, end - start + 1);
    }

    public static ushort[] DecodeDataWriteWords(string text, int count)
    {
        if (text.Length < 11 + count * 4)
        {
            throw new ZeusProtocolException("MEWTOCOL WD 写字请求数据长度不足。");
        }

        return DecodeWords(text, 11, count);
    }

    public static ushort[] DecodeContactWriteWords(string text, int count)
    {
        if (text.Length < 10 + count * 4)
        {
            throw new ZeusProtocolException("MEWTOCOL WC 写接点字请求数据长度不足。");
        }

        return DecodeWords(text, 10, count);
    }

    public static ushort[] DecodeWordRead(string text, int count)
    {
        if (text.Length < count * 4)
        {
            throw new ZeusProtocolException("MEWTOCOL 读字响应长度不足。");
        }

        return DecodeWords(text, 0, count);
    }

    public static string EncodeWords(IReadOnlyList<ushort> values)
    {
        var builder = new StringBuilder(values.Count * 4);
        AppendWords(builder, values);
        return builder.ToString();
    }

    public static int GetWordCount(MewtocolDataType dataType)
        => dataType switch
        {
            MewtocolDataType.Bit => 1,
            MewtocolDataType.Word or MewtocolDataType.Int16 => 1,
            MewtocolDataType.UInt32 or MewtocolDataType.Int32 or MewtocolDataType.Real => 2,
            _ => throw new ZeusProtocolException($"不支持的 MEWTOCOL 数据类型：{dataType}。")
        };

    public static object DecodeValue(MewtocolDataType dataType, IReadOnlyList<ushort> words, byte bitOffset, double? scale, MewtocolWordOrder wordOrder)
    {
        if (words.Count < GetWordCount(dataType))
        {
            throw new ZeusProtocolException($"MEWTOCOL {dataType} 解码需要 {GetWordCount(dataType)} 个字。");
        }

        object raw = dataType switch
        {
            MewtocolDataType.Bit => (words[0] & (1 << bitOffset)) != 0,
            MewtocolDataType.Word => words[0],
            MewtocolDataType.Int16 => unchecked((short)words[0]),
            MewtocolDataType.UInt32 => CombineUInt32(words[0], words[1], wordOrder),
            MewtocolDataType.Int32 => unchecked((int)CombineUInt32(words[0], words[1], wordOrder)),
            MewtocolDataType.Real => BitConverter.Int32BitsToSingle(unchecked((int)CombineUInt32(words[0], words[1], wordOrder))),
            _ => throw new ZeusProtocolException($"不支持的 MEWTOCOL 数据类型：{dataType}。")
        };

        if (scale is null || raw is bool)
        {
            return raw;
        }

        var number = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        return number * scale.Value;
    }

    public static ushort[] EncodeValue(MewtocolDataType dataType, object value, double? scale, MewtocolWordOrder wordOrder)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (dataType == MewtocolDataType.Bit)
        {
            throw new ZeusProtocolException("MEWTOCOL Bit 值不能编码为完整字数组，请使用读改写流程。");
        }

        var actual = scale is { } factor
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture) / factor
            : value;

        return dataType switch
        {
            MewtocolDataType.Word => [ConvertToUInt16(actual, dataType)],
            MewtocolDataType.Int16 => [unchecked((ushort)ConvertToInt16(actual, dataType))],
            MewtocolDataType.UInt32 => SplitUInt32(ConvertToUInt32(actual, dataType), wordOrder),
            MewtocolDataType.Int32 => SplitUInt32(unchecked((uint)ConvertToInt32(actual, dataType)), wordOrder),
            MewtocolDataType.Real => SplitUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(ConvertToSingle(actual, dataType))), wordOrder),
            _ => throw new ZeusProtocolException($"不支持的 MEWTOCOL 数据类型：{dataType}。")
        };
    }

    public static bool IsWriteCommand(string command)
        => command is WriteData or WriteContact;

    public static char DataAreaCode(MewtocolDataArea area)
        => area switch
        {
            MewtocolDataArea.DataRegister => 'D',
            MewtocolDataArea.LinkDataRegister => 'L',
            MewtocolDataArea.FileRegister => 'F',
            _ => throw new ZeusProtocolException($"不支持的 MEWTOCOL 数据区：{area}。")
        };

    public static char ContactAreaCode(MewtocolContactArea area)
        => area switch
        {
            MewtocolContactArea.ExternalInput => 'X',
            MewtocolContactArea.ExternalOutput => 'Y',
            MewtocolContactArea.InternalRelay => 'R',
            MewtocolContactArea.LinkRelay => 'L',
            _ => throw new ZeusProtocolException($"不支持的 MEWTOCOL 接点区：{area}。")
        };

    private static bool TryDecodeFrame(ReadOnlySpan<byte> frame, out byte station, out char marker, out string command, out string text)
    {
        station = 0;
        marker = '\0';
        command = string.Empty;
        text = string.Empty;
        if (frame.Length < 8 || frame[0] != (byte)'%' || frame[^1] != (byte)'\r')
        {
            return false;
        }

        var ascii = Encoding.ASCII.GetString(frame);
        if (!byte.TryParse(ascii.Substring(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out station))
        {
            return false;
        }

        var bccStart = ascii.Length - 3;
        if (!byte.TryParse(ascii.Substring(bccStart, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var expectedBcc))
        {
            return false;
        }

        var body = ascii[..bccStart];
        if (ComputeBcc(body) != expectedBcc)
        {
            return false;
        }

        marker = ascii[3];
        if (marker == '!')
        {
            if (body.Length != 6)
            {
                return false;
            }

            text = body[4..6];
            return true;
        }

        if (body.Length < 6 || marker is not ('$' or '#'))
        {
            return false;
        }

        command = body.Substring(4, 2);
        text = body[6..];
        return true;
    }

    private static bool TryDataAreaFromCode(char code, out MewtocolDataArea area)
    {
        switch (char.ToUpperInvariant(code))
        {
            case 'D':
                area = MewtocolDataArea.DataRegister;
                return true;
            case 'L':
                area = MewtocolDataArea.LinkDataRegister;
                return true;
            case 'F':
                area = MewtocolDataArea.FileRegister;
                return true;
            default:
                area = default;
                return false;
        }
    }

    private static bool TryContactAreaFromCode(char code, out MewtocolContactArea area)
    {
        switch (char.ToUpperInvariant(code))
        {
            case 'X':
                area = MewtocolContactArea.ExternalInput;
                return true;
            case 'Y':
                area = MewtocolContactArea.ExternalOutput;
                return true;
            case 'R':
                area = MewtocolContactArea.InternalRelay;
                return true;
            case 'L':
                area = MewtocolContactArea.LinkRelay;
                return true;
            default:
                area = default;
                return false;
        }
    }

    private static byte ComputeBcc(string body)
    {
        byte bcc = 0;
        foreach (var value in Encoding.ASCII.GetBytes(body))
        {
            bcc ^= value;
        }

        return bcc;
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

    private static ushort[] DecodeWords(string text, int offset, int count)
    {
        var values = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ParseHexWord(text.Substring(offset + i * 4, 4));
        }

        return values;
    }

    private static void AppendWords(StringBuilder builder, IReadOnlyList<ushort> values)
    {
        foreach (var value in values)
        {
            builder.Append(value.ToString("X4", CultureInfo.InvariantCulture));
        }
    }

    private static ushort ParseHexWord(string text)
    {
        if (!ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            throw new ZeusProtocolException($"MEWTOCOL 字数据「{text}」不是 4 位十六进制数。");
        }

        return value;
    }

    private static void ValidateStationNumber(byte stationNumber)
    {
        if (stationNumber is < 1 or > 99)
        {
            throw new ZeusProtocolException($"MEWTOCOL 站号必须介于 1 与 99 之间，当前为 {stationNumber}。");
        }
    }

    private static void EnsureDataAddress(int address)
    {
        if (address is < 0 or > 99999)
        {
            throw new ZeusProtocolException($"MEWTOCOL 数据寄存器地址必须介于 0 与 99999 之间，当前为 {address}。");
        }
    }

    private static void EnsureContactWordAddress(int address)
    {
        if (address is < 0 or > 9999)
        {
            throw new ZeusProtocolException($"MEWTOCOL 接点字地址必须介于 0 与 9999 之间，当前为 {address}。");
        }
    }

    private static void EnsureCount(int count)
    {
        if (count is < 1 or > 9999)
        {
            throw new ZeusProtocolException($"MEWTOCOL 读取/写入数量必须在 1 到 9999 之间，当前为 {count}。");
        }
    }

    private static uint CombineUInt32(ushort first, ushort second, MewtocolWordOrder wordOrder)
        => wordOrder == MewtocolWordOrder.HighWordFirst
            ? ((uint)first << 16) | second
            : ((uint)second << 16) | first;

    private static ushort[] SplitUInt32(uint value, MewtocolWordOrder wordOrder)
    {
        var high = (ushort)(value >> 16);
        var low = (ushort)(value & 0xFFFF);
        return wordOrder == MewtocolWordOrder.HighWordFirst ? [high, low] : [low, high];
    }

    private static ushort ConvertToUInt16(object value, MewtocolDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < ushort.MinValue or > ushort.MaxValue)
        {
            throw new ZeusProtocolException($"MEWTOCOL {dataType} 写入值 {value} 超出 UInt16 范围。");
        }

        return (ushort)number;
    }

    private static short ConvertToInt16(object value, MewtocolDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < short.MinValue or > short.MaxValue)
        {
            throw new ZeusProtocolException($"MEWTOCOL {dataType} 写入值 {value} 超出 Int16 范围。");
        }

        return (short)number;
    }

    private static uint ConvertToUInt32(object value, MewtocolDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < uint.MinValue or > uint.MaxValue)
        {
            throw new ZeusProtocolException($"MEWTOCOL {dataType} 写入值 {value} 超出 UInt32 范围。");
        }

        return (uint)number;
    }

    private static int ConvertToInt32(object value, MewtocolDataType dataType)
    {
        var number = Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        if (number is < int.MinValue or > int.MaxValue)
        {
            throw new ZeusProtocolException($"MEWTOCOL {dataType} 写入值 {value} 超出 Int32 范围。");
        }

        return (int)number;
    }

    private static float ConvertToSingle(object value, MewtocolDataType dataType)
    {
        var number = Convert.ToSingle(value, CultureInfo.InvariantCulture);
        if (!float.IsFinite(number))
        {
            throw new ZeusProtocolException($"MEWTOCOL {dataType} 写入值必须是有限数值。");
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

internal readonly record struct MewtocolRequestFrame(byte StationNumber, string Command, string Text);

internal readonly record struct MewtocolResponseFrame(byte StationNumber, string Command, byte ErrorCode, string Text)
{
    public bool IsError => ErrorCode != 0;
}
