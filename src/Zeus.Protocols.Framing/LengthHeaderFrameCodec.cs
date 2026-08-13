namespace Zeus;

/// <summary>
/// 按 <see cref="FrameLayout"/> 编解码。解码时在流中搜索帧头，校验失败会丢弃该候选并继续扫描，避免一次坏帧卡死后续数据。
/// </summary>
public sealed class LengthHeaderFrameCodec : IFrameCodec
{
    private readonly FrameLayout _layout;
    private readonly List<byte> _buffer = [];

    /// <summary>
    /// 使用指定布局创建编解码器。每个会话应使用独立实例，因为内部缓冲不是线程安全的。
    /// </summary>
    /// <param name="layout">帧布局。为 <c>null</c> 时使用默认 <c>AA 55</c> + 单字节长度。</param>
    public LengthHeaderFrameCodec(FrameLayout? layout = null)
    {
        _layout = layout ?? new FrameLayout();
    }

    /// <inheritdoc />
    public byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > _layout.MaxPayloadLength)
        {
            throw new ZeusProtocolException(
                $"载荷长度为 {payload.Length}，超过当前长度域上限 {_layout.MaxPayloadLength}。请改用双字节长度域或拆分报文。");
        }

        var header = _layout.Header;
        var lengthSize = _layout.LengthFieldSize;
        var checksumSize = _layout.ChecksumSize;
        var frame = new byte[header.Count + lengthSize + payload.Length + checksumSize];
        for (var i = 0; i < header.Count; i++)
        {
            frame[i] = header[i];
        }

        WriteLength(frame.AsSpan(header.Count, lengthSize), payload.Length);
        payload.CopyTo(frame.AsSpan(header.Count + lengthSize));
        var checksum = FrameChecksum.Compute(
            _layout.Checksum,
            frame.AsSpan(header.Count, lengthSize + payload.Length));
        checksum.CopyTo(frame.AsSpan(header.Count + lengthSize + payload.Length));
        return frame;
    }

    /// <inheritdoc />
    public void Append(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            _buffer.Add(value);
        }
    }

    /// <inheritdoc />
    public bool TryDecode(out byte[] payload)
    {
        payload = [];
        var header = _layout.Header;
        var headerLength = header.Count;
        var lengthSize = _layout.LengthFieldSize;
        var checksumSize = _layout.ChecksumSize;

        while (_buffer.Count >= headerLength + lengthSize)
        {
            var headerIndex = IndexOfHeader();
            if (headerIndex < 0)
            {
                KeepPossibleHeaderPrefix();
                return false;
            }

            if (headerIndex > 0)
            {
                _buffer.RemoveRange(0, headerIndex);
            }

            if (_buffer.Count < headerLength + lengthSize)
            {
                return false;
            }

            var payloadLength = ReadLength(_buffer, headerLength);
            if (payloadLength < 0 || payloadLength > _layout.MaxPayloadLength)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            var total = headerLength + lengthSize + payloadLength + checksumSize;
            if (_buffer.Count < total)
            {
                return false;
            }

            var covered = new byte[lengthSize + payloadLength];
            _buffer.CopyTo(headerLength, covered, 0, covered.Length);
            var expected = FrameChecksum.Compute(_layout.Checksum, covered);
            if (!ChecksumMatches(expected, headerLength + lengthSize + payloadLength))
            {
                _buffer.RemoveAt(0);
                continue;
            }

            payload = new byte[payloadLength];
            _buffer.CopyTo(headerLength + lengthSize, payload, 0, payloadLength);
            _buffer.RemoveRange(0, total);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Reset() => _buffer.Clear();

    private int IndexOfHeader()
    {
        var header = _layout.Header;
        var lastStart = _buffer.Count - header.Count;
        for (var i = 0; i <= lastStart; i++)
        {
            var matched = true;
            for (var j = 0; j < header.Count; j++)
            {
                if (_buffer[i + j] != header[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return i;
            }
        }

        return -1;
    }

    private void KeepPossibleHeaderPrefix()
    {
        var header = _layout.Header;
        var keep = 0;
        var max = Math.Min(header.Count - 1, _buffer.Count);
        for (var prefix = max; prefix >= 1; prefix--)
        {
            var ok = true;
            for (var i = 0; i < prefix; i++)
            {
                if (_buffer[_buffer.Count - prefix + i] != header[i])
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                keep = prefix;
                break;
            }
        }

        if (keep < _buffer.Count)
        {
            _buffer.RemoveRange(0, _buffer.Count - keep);
        }
    }

    private void WriteLength(Span<byte> dest, int length)
    {
        switch (_layout.LengthKind)
        {
            case FrameLengthKind.UInt8:
                dest[0] = (byte)length;
                break;
            case FrameLengthKind.UInt16LittleEndian:
                dest[0] = (byte)(length & 0xFF);
                dest[1] = (byte)(length >> 8);
                break;
            case FrameLengthKind.UInt16BigEndian:
                dest[0] = (byte)(length >> 8);
                dest[1] = (byte)(length & 0xFF);
                break;
        }
    }

    private int ReadLength(List<byte> buffer, int offset)
    {
        return _layout.LengthKind switch
        {
            FrameLengthKind.UInt8 => buffer[offset],
            FrameLengthKind.UInt16LittleEndian => buffer[offset] | (buffer[offset + 1] << 8),
            FrameLengthKind.UInt16BigEndian => (buffer[offset] << 8) | buffer[offset + 1],
            _ => -1
        };
    }

    private bool ChecksumMatches(byte[] expected, int offset)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (_buffer[offset + i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }
}
