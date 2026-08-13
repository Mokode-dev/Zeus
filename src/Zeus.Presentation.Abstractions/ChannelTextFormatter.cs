using System.Text;

namespace Zeus;

/// <summary>
/// 将通道原始字节格式化为适合标签展示的文本。
/// 可打印 ASCII 按字符显示，否则显示十六进制，避免二进制报文把界面打成乱码。
/// </summary>
public static class ChannelTextFormatter
{
    /// <summary>
    /// 默认格式：可打印 ASCII（含常见空白）原样输出，否则以 <c>-</c> 分隔的大写十六进制输出。
    /// </summary>
    /// <param name="data">通道载荷。</param>
    /// <returns>可放入 <c>Control.Text</c> 或 <c>TextBlock.Text</c> 的字符串；空载荷返回空字符串。</returns>
    public static string Default(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.IsEmpty)
        {
            return string.Empty;
        }

        return IsPrintableAscii(span) ? Encoding.ASCII.GetString(span) : Convert.ToHexString(span);
    }

    /// <summary>
    /// 始终输出无分隔的大写十六进制，便于对照报文。
    /// </summary>
    /// <param name="data">通道载荷。</param>
    public static string Hex(ReadOnlyMemory<byte> data)
    {
        return data.IsEmpty ? string.Empty : Convert.ToHexString(data.Span);
    }

    private static bool IsPrintableAscii(ReadOnlySpan<byte> span)
    {
        foreach (var value in span)
        {
            var isPrintable = value is >= 0x20 and <= 0x7E or (byte)'\r' or (byte)'\n' or (byte)'\t';
            if (!isPrintable)
            {
                return false;
            }
        }

        return true;
    }
}
