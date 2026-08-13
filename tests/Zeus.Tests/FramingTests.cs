using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证自定义帧的粘包拆包、校验与请求-响应会话。
/// </summary>
public sealed class FramingTests
{
    /// <summary>
    /// 编码后再解码必须还原载荷。
    /// </summary>
    [Fact]
    public void Codec_RoundTripsPayload()
    {
        var codec = new LengthHeaderFrameCodec(new FrameLayout(
            [0xAA, 0x55],
            FrameLengthKind.UInt8,
            FrameChecksumKind.Crc16Modbus));
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        codec.Append(codec.Encode(payload));
        Assert.True(codec.TryDecode(out var decoded));
        Assert.Equal(payload, decoded);
        Assert.False(codec.TryDecode(out _));
    }

    /// <summary>
    /// 一次到达两帧（粘包）应能连续取出。
    /// </summary>
    [Fact]
    public void Codec_DecodesStickyPackets()
    {
        var codec = new LengthHeaderFrameCodec();
        var first = codec.Encode([0x11]);
        var second = codec.Encode([0x22, 0x33]);
        codec.Append(first.Concat(second).ToArray());
        Assert.True(codec.TryDecode(out var a));
        Assert.True(codec.TryDecode(out var b));
        Assert.Equal(new byte[] { 0x11 }, a);
        Assert.Equal(new byte[] { 0x22, 0x33 }, b);
    }

    /// <summary>
    /// 半包应等到后续字节到齐后再输出。
    /// </summary>
    [Fact]
    public void Codec_WaitsForSplitPackets()
    {
        var codec = new LengthHeaderFrameCodec();
        var frame = codec.Encode([0x42]);
        codec.Append(frame.AsSpan(0, 2));
        Assert.False(codec.TryDecode(out _));
        codec.Append(frame.AsSpan(2));
        Assert.True(codec.TryDecode(out var payload));
        Assert.Equal(new byte[] { 0x42 }, payload);
    }

    /// <summary>
    /// 校验错误的候选帧应被丢弃，后续好帧仍可解码。
    /// </summary>
    [Fact]
    public void Codec_SkipsBadChecksumAndContinues()
    {
        var layout = new FrameLayout([0xAA], FrameLengthKind.UInt8, FrameChecksumKind.Xor8);
        var codec = new LengthHeaderFrameCodec(layout);
        var good = codec.Encode([0x10]);
        var broken = (byte[])good.Clone();
        broken[^1] ^= 0xFF;
        codec.Append(broken.Concat(good).ToArray());
        Assert.True(codec.TryDecode(out var payload));
        Assert.Equal(new byte[] { 0x10 }, payload);
    }

    /// <summary>
    /// 虚拟通道回显完整帧时，会话应返回原始载荷。
    /// </summary>
    [Fact]
    public async Task FrameSession_RequestReturnsEchoedPayload()
    {
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("bus"));
        await using var session = host.CreateFrameSession("bus");
        await host.StartAsync();

        var reply = await session.RequestAsync(new byte[] { 0xDE, 0xAD });
        Assert.Equal(new byte[] { 0xDE, 0xAD }, reply);
    }
}
