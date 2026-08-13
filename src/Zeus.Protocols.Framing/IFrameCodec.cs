namespace Zeus;

/// <summary>
/// 帧编解码契约。编码器把业务载荷变成线上字节；解码器从字节流中抽出完整载荷。
/// </summary>
public interface IFrameCodec
{
    /// <summary>
    /// 将业务载荷封装为可写入通道的完整帧。
    /// </summary>
    /// <param name="payload">业务载荷，不含帧头与校验。</param>
    byte[] Encode(ReadOnlySpan<byte> payload);

    /// <summary>
    /// 把刚收到的原始字节追加进解码缓冲。
    /// </summary>
    /// <param name="data">通道 <c>DataReceived</c> 给出的片段，可能是半包或粘包。</param>
    void Append(ReadOnlySpan<byte> data);

    /// <summary>
    /// 尝试取出一帧完整载荷。可循环调用直到返回 <c>false</c>。
    /// </summary>
    /// <param name="payload">成功时为载荷拷贝。</param>
    /// <returns>缓冲中已有完整且校验通过的一帧时为 <c>true</c>。</returns>
    bool TryDecode(out byte[] payload);

    /// <summary>丢弃未完成的半包，通常在通道故障后调用。</summary>
    void Reset();
}
