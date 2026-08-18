using System.Globalization;
using System.Text;

namespace Zeus;

/// <summary>
/// DL/T 645-2007 帧编解码。处理地址反序、0x33 数据偏移、校验和与常用数据项转换。
/// </summary>
internal static class Dlt645Codec
{
    public const byte ReadData = 0x11;
    public const byte WriteData = 0x14;
    public const byte ReadDataResponse = 0x91;
    public const byte WriteDataResponse = 0x94;

    public static byte[] EncodeReadDataRequest(string meterAddress, uint dataIdentifier, int wakeUpPreambleCount)
        => EncodeFrame(meterAddress, ReadData, EncodeDataIdentifier(dataIdentifier), wakeUpPreambleCount);

    public static byte[] EncodeWriteDataRequest(
        string meterAddress,
        uint dataIdentifier,
        IReadOnlyList<byte> data,
        string password,
        string operatorCode,
        int wakeUpPreambleCount)
    {
        ArgumentNullException.ThrowIfNull(data);
        var body = new List<byte>(4 + 8 + data.Count);
        body.AddRange(EncodeDataIdentifier(dataIdentifier));
        body.AddRange(EncodeBcdDigits(password, 4, nameof(password)));
        body.AddRange(EncodeBcdDigits(operatorCode, 4, nameof(operatorCode)));
        body.AddRange(data);
        return EncodeFrame(meterAddress, WriteData, body, wakeUpPreambleCount);
    }

    public static byte[] EncodeResponse(string meterAddress, byte controlCode, IReadOnlyList<byte> data)
        => EncodeFrame(meterAddress, controlCode, data, wakeUpPreambleCount: 0);

    public static bool TryDecodeFrame(IReadOnlyList<byte> buffer, out Dlt645Frame frame, out int consumed)
    {
        frame = default;
        consumed = 0;
        var start = IndexOf(buffer, 0x68, 0);
        if (start < 0)
        {
            return false;
        }

        if (buffer.Count - start < 12)
        {
            return false;
        }

        if (buffer[start + 7] != 0x68)
        {
            throw new ZeusProtocolException("DL/T 645 帧缺少第二个 0x68。请确认通道连接的是 DL/T 645 设备。");
        }

        var length = buffer[start + 9];
        var frameLength = 12 + length;
        if (buffer.Count - start < frameLength)
        {
            return false;
        }

        var endIndex = start + frameLength - 1;
        if (buffer[endIndex] != 0x16)
        {
            throw new ZeusProtocolException("DL/T 645 帧结束符不是 0x16。请检查波特率、校验位或协议类型。");
        }

        var expectedChecksum = buffer[start + frameLength - 2];
        var actualChecksum = ComputeChecksum(buffer, start, frameLength - 2);
        if (expectedChecksum != actualChecksum)
        {
            throw new ZeusProtocolException($"DL/T 645 校验和错误，收到 0x{expectedChecksum:X2}，计算为 0x{actualChecksum:X2}。");
        }

        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = unchecked((byte)(buffer[start + 10 + i] - 0x33));
        }

        frame = new Dlt645Frame(DecodeAddress(buffer, start + 1), buffer[start + 8], data);
        consumed = start + frameLength;
        return true;
    }

    public static uint DecodeDataIdentifier(IReadOnlyList<byte> data, int offset = 0)
    {
        if (data.Count - offset < 4)
        {
            throw new ZeusProtocolException("DL/T 645 数据项标识必须为 4 字节。");
        }

        return (uint)(data[offset]
            | (data[offset + 1] << 8)
            | (data[offset + 2] << 16)
            | (data[offset + 3] << 24));
    }

    public static byte[] EncodeDataIdentifier(uint dataIdentifier)
        =>
        [
            (byte)(dataIdentifier & 0xFF),
            (byte)((dataIdentifier >> 8) & 0xFF),
            (byte)((dataIdentifier >> 16) & 0xFF),
            (byte)((dataIdentifier >> 24) & 0xFF)
        ];

    public static double DecodeBcd(IReadOnlyList<byte> data, double scale)
    {
        if (data.Count == 0)
        {
            throw new ZeusProtocolException("DL/T 645 BCD 数据不能为空。");
        }

        EnsureScale(scale);
        var digits = new StringBuilder(data.Count * 2);
        for (var i = data.Count - 1; i >= 0; i--)
        {
            var high = data[i] >> 4;
            var low = data[i] & 0x0F;
            if (high > 9 || low > 9)
            {
                throw new ZeusProtocolException($"DL/T 645 BCD 字节 0x{data[i]:X2} 非法。");
            }

            digits.Append((char)('0' + high));
            digits.Append((char)('0' + low));
        }

        var raw = long.Parse(digits.ToString(), CultureInfo.InvariantCulture);
        return raw * scale;
    }

    public static byte[] EncodeBcd(double value, int byteLength, double scale)
    {
        EnsureDataLength(byteLength);
        EnsureScale(scale);
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ZeusProtocolException("DL/T 645 BCD 写入值必须是非负有限数值。");
        }

        var raw = Math.Round(value / scale, MidpointRounding.AwayFromZero);
        var maxDigits = byteLength * 2;
        var digits = raw.ToString("0", CultureInfo.InvariantCulture);
        if (digits.Length > maxDigits)
        {
            throw new ZeusProtocolException($"DL/T 645 BCD 写入值 {value} 超过 {byteLength} 字节可表示范围。");
        }

        digits = digits.PadLeft(maxDigits, '0');
        var result = new byte[byteLength];
        for (var i = 0; i < byteLength; i++)
        {
            var lowDigitIndex = digits.Length - 1 - i * 2;
            var low = digits[lowDigitIndex] - '0';
            var high = digits[lowDigitIndex - 1] - '0';
            result[i] = (byte)((high << 4) | low);
        }

        return result;
    }

    public static string FormatDataIdentifier(uint dataIdentifier)
        => "0x" + dataIdentifier.ToString("X8", CultureInfo.InvariantCulture);

    public static void ValidateAddress(string meterAddress)
        => _ = EncodeAddress(meterAddress);

    public static void EnsureDataLength(int dataLength)
    {
        if (dataLength is < 1 or > 64)
        {
            throw new ZeusProtocolException($"DL/T 645 数据长度必须介于 1 与 64 字节之间，当前为 {dataLength}。");
        }
    }

    public static void EnsureScale(double scale)
    {
        if (scale <= 0 || !double.IsFinite(scale))
        {
            throw new ZeusProtocolException("DL/T 645 scale 必须是大于 0 的有限数值。");
        }
    }

    public static byte[] ParseHexData(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ZeusProtocolException("DL/T 645 十六进制数据不能为空。");
        }

        var normalized = text.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        if (normalized.Length % 2 != 0)
        {
            throw new ZeusProtocolException("DL/T 645 十六进制数据长度必须为偶数。");
        }

        var result = new byte[normalized.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            if (!byte.TryParse(normalized.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
            {
                throw new ZeusProtocolException($"DL/T 645 十六进制数据「{text}」格式无效。");
            }
        }

        return result;
    }

    private static byte[] EncodeFrame(string meterAddress, byte controlCode, IReadOnlyList<byte> data, int wakeUpPreambleCount)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (wakeUpPreambleCount is < 0 or > 16)
        {
            throw new ZeusProtocolException($"DL/T 645 前导 0xFE 数量必须介于 0 与 16 之间，当前为 {wakeUpPreambleCount}。");
        }

        if (data.Count > byte.MaxValue)
        {
            throw new ZeusProtocolException("DL/T 645 单帧数据区不能超过 255 字节。");
        }

        var address = EncodeAddress(meterAddress);
        var frame = new byte[wakeUpPreambleCount + 12 + data.Count];
        Array.Fill(frame, (byte)0xFE, 0, wakeUpPreambleCount);
        var offset = wakeUpPreambleCount;
        frame[offset] = 0x68;
        Array.Copy(address, 0, frame, offset + 1, address.Length);
        frame[offset + 7] = 0x68;
        frame[offset + 8] = controlCode;
        frame[offset + 9] = (byte)data.Count;
        for (var i = 0; i < data.Count; i++)
        {
            frame[offset + 10 + i] = unchecked((byte)(data[i] + 0x33));
        }

        frame[offset + 10 + data.Count] = ComputeChecksum(frame, offset, 10 + data.Count);
        frame[offset + 11 + data.Count] = 0x16;
        return frame;
    }

    private static byte[] EncodeAddress(string meterAddress)
    {
        if (string.IsNullOrWhiteSpace(meterAddress))
        {
            throw new ZeusProtocolException("DL/T 645 表地址不能为空，应为 12 位十进制字符串。");
        }

        var normalized = meterAddress.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != 12 || normalized.Any(ch => ch is < '0' or > '9'))
        {
            throw new ZeusProtocolException($"DL/T 645 表地址「{meterAddress}」无效，应为 12 位十进制字符串，例如 000000000001。");
        }

        var result = new byte[6];
        for (var i = 0; i < result.Length; i++)
        {
            var high = normalized[normalized.Length - 2 - i * 2] - '0';
            var low = normalized[normalized.Length - 1 - i * 2] - '0';
            result[i] = (byte)((high << 4) | low);
        }

        return result;
    }

    private static string DecodeAddress(IReadOnlyList<byte> buffer, int offset)
    {
        var builder = new StringBuilder(12);
        for (var i = 5; i >= 0; i--)
        {
            var value = buffer[offset + i];
            var high = value >> 4;
            var low = value & 0x0F;
            if (high > 9 || low > 9)
            {
                throw new ZeusProtocolException($"DL/T 645 地址 BCD 字节 0x{value:X2} 非法。");
            }

            builder.Append((char)('0' + high));
            builder.Append((char)('0' + low));
        }

        return builder.ToString();
    }

    private static byte[] EncodeBcdDigits(string text, int byteLength, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ZeusProtocolException($"DL/T 645 {name} 不能为空。");
        }

        var normalized = text.Trim();
        if (normalized.Length != byteLength * 2 || normalized.Any(ch => ch is < '0' or > '9'))
        {
            throw new ZeusProtocolException($"DL/T 645 {name} 必须是 {byteLength * 2} 位十进制字符串。");
        }

        return EncodeBcd(double.Parse(normalized, CultureInfo.InvariantCulture), byteLength, 1);
    }

    private static byte ComputeChecksum(IReadOnlyList<byte> buffer, int offset, int count)
    {
        var sum = 0;
        for (var i = 0; i < count; i++)
        {
            sum += buffer[offset + i];
        }

        return (byte)(sum & 0xFF);
    }

    private static int IndexOf(IReadOnlyList<byte> buffer, byte value, int start)
    {
        for (var i = start; i < buffer.Count; i++)
        {
            if (buffer[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}

internal readonly record struct Dlt645Frame(string MeterAddress, byte ControlCode, byte[] Data)
{
    public bool IsError => (ControlCode & 0x40) != 0;
}
