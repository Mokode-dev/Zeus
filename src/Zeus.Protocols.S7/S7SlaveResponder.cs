namespace Zeus;

/// <summary>
/// Siemens S7 虚拟 PLC。实现 <see cref="IVirtualResponder"/>，可直接交给 <c>AddVirtualChannel</c>。
/// </summary>
public sealed class S7SlaveResponder : IVirtualResponder
{
    private readonly S7Options _options;
    private readonly S7SlaveMemory _memory;

    /// <summary>
    /// 创建虚拟 PLC。
    /// </summary>
    /// <param name="memory">内存映像。为 <c>null</c> 时使用默认容量。</param>
    /// <param name="options">S7 会话选项。为 <c>null</c> 时使用默认 rack/slot。</param>
    public S7SlaveResponder(S7SlaveMemory? memory = null, S7Options? options = null)
    {
        _memory = memory ?? new S7SlaveMemory();
        _options = options ?? new S7Options();
    }

    /// <summary>可在测试中预置或断言的映像。</summary>
    public S7SlaveMemory Memory => _memory;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Respond(ReadOnlyMemory<byte> request)
    {
        var frame = request.Span;
        if (S7Codec.IsConnectionRequest(frame))
        {
            return S7Codec.EncodeConnectionConfirm(_options);
        }

        if (S7Codec.TryDecodeSetupCommunicationRequest(frame, out var setupPdu, out var requestedPduLength))
        {
            var pduLength = requestedPduLength == 0 ? _options.RequestedPduLength : requestedPduLength;
            return S7Codec.EncodeSetupCommunicationResponse(setupPdu, pduLength);
        }

        if (S7Codec.TryDecodeReadVarRequest(frame, out var readPdu, out var readItems))
        {
            var values = new byte[]?[readItems.Length];
            for (var i = 0; i < readItems.Length; i++)
            {
                values[i] = TryRead(readItems[i]);
            }

            return S7Codec.EncodeReadVarResponse(readPdu, values);
        }

        if (S7Codec.TryDecodeWriteVarRequest(frame, out var writePdu, out var writeItems, out var writeValues))
        {
            var results = new bool[writeItems.Length];
            for (var i = 0; i < writeItems.Length; i++)
            {
                results[i] = TryWrite(writeItems[i], writeValues[i]);
            }

            return S7Codec.EncodeWriteVarResponse(writePdu, results);
        }

        return null;
    }

    private byte[]? TryRead(S7VariableAddress item)
    {
        try
        {
            var table = GetTable(item, item.ByteOffset + item.ByteLength);
            if (item.ByteOffset < 0 || item.ByteOffset + item.ByteLength > table.Length)
            {
                return null;
            }

            if (item.DataType == S7DataType.Bool)
            {
                var mask = 1 << item.BitOffset;
                return [(byte)((table[item.ByteOffset] & mask) != 0 ? 1 : 0)];
            }

            return table.AsSpan(item.ByteOffset, item.ByteLength).ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool TryWrite(S7VariableAddress item, byte[] value)
    {
        try
        {
            var table = GetTable(item, item.ByteOffset + Math.Max(item.ByteLength, value.Length));
            if (item.DataType == S7DataType.Bool)
            {
                var mask = (byte)(1 << item.BitOffset);
                if (value.Length > 0 && value[0] != 0)
                {
                    table[item.ByteOffset] |= mask;
                }
                else
                {
                    table[item.ByteOffset] &= (byte)~mask;
                }

                return true;
            }

            if (item.ByteOffset < 0 || item.ByteOffset + value.Length > table.Length)
            {
                return false;
            }

            value.CopyTo(table.AsSpan(item.ByteOffset));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private byte[] GetTable(S7VariableAddress item, int minimumSize)
        => item.Area switch
        {
            S7Area.Inputs => _memory.Inputs,
            S7Area.Outputs => _memory.Outputs,
            S7Area.Merkers => _memory.Markers,
            S7Area.DataBlock => _memory.GetDataBlock(item.DbNumber, minimumSize),
            _ => throw new ZeusProtocolException($"不支持的 S7 存储区：{item.Area}。")
        };
}
