namespace Zeus;

/// <summary>
/// 自定义帧布局：<c>[帧头][长度][载荷][校验]</c>。
/// 未指定帧头时默认 <c>AA 55</c>，这是工控自定义协议里最常见的同步字。
/// </summary>
public sealed class FrameLayout
{
    /// <summary>
    /// 使用默认帧头 <c>AA 55</c>、单字节长度、无校验。
    /// </summary>
    public FrameLayout()
        : this(new byte[] { 0xAA, 0x55 }, FrameLengthKind.UInt8, FrameChecksumKind.None)
    {
    }

    /// <summary>
    /// 指定帧头、长度域与校验。
    /// </summary>
    /// <param name="header">同步字，至少 1 字节。</param>
    /// <param name="lengthKind">长度域宽度与端序。</param>
    /// <param name="checksum">帧尾校验。</param>
    public FrameLayout(byte[] header, FrameLengthKind lengthKind, FrameChecksumKind checksum)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.Length == 0)
        {
            throw new ZeusException("帧头不能为空。请至少指定一个同步字节，例如 0xAA。");
        }

        Header = header.ToArray();
        LengthKind = lengthKind;
        Checksum = checksum;
    }

    /// <summary>同步字。解码时会在流中搜索该序列。</summary>
    public IReadOnlyList<byte> Header { get; }

    /// <summary>长度域类型。</summary>
    public FrameLengthKind LengthKind { get; }

    /// <summary>校验算法。</summary>
    public FrameChecksumKind Checksum { get; }

    /// <summary>长度域占用的字节数。</summary>
    public int LengthFieldSize => LengthKind == FrameLengthKind.UInt8 ? 1 : 2;

    /// <summary>校验域占用的字节数。</summary>
    public int ChecksumSize => Checksum switch
    {
        FrameChecksumKind.None => 0,
        FrameChecksumKind.Crc16Modbus => 2,
        _ => 1
    };

    /// <summary>该布局允许的最大载荷长度。</summary>
    public int MaxPayloadLength => LengthKind == FrameLengthKind.UInt8 ? 255 : 65535;
}
