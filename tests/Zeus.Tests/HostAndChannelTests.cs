using System.Text;
using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证宿主启停、虚拟通道回显以及目录查找的失败信息是否可操作。
/// </summary>
public sealed class HostAndChannelTests
{
    /// <summary>
    /// 虚拟通道在宿主启动后应处于打开状态，写入的字节必须原样回显。
    /// </summary>
    [Fact]
    public async Task VirtualChannel_EchoesWrittenBytes()
    {
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("loop"));
        var channel = host.Channels.Get("loop");
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, e) => received.TrySetResult(e.Data.ToArray());

        await host.StartAsync();
        Assert.Equal(ChannelState.Open, channel.State);

        var payload = Encoding.ASCII.GetBytes("PING");
        await channel.WriteAsync(payload);

        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(payload, actual);

        await host.StopAsync();
        Assert.Equal(ChannelState.Closed, channel.State);
    }

    /// <summary>
    /// 重复打开已打开的通道必须幂等，不能再次触发 Opening 迁移。
    /// </summary>
    [Fact]
    public async Task OpenAsync_IsIdempotentWhenAlreadyOpen()
    {
        var channel = new VirtualChannel("once");
        var transitions = new List<(ChannelState Previous, ChannelState Current)>();
        channel.StateChanged += (_, e) => transitions.Add((e.Previous, e.Current));

        await channel.OpenAsync();
        await channel.OpenAsync();

        Assert.Equal(ChannelState.Open, channel.State);
        Assert.Equal(new[]
        {
            (ChannelState.Created, ChannelState.Opening),
            (ChannelState.Opening, ChannelState.Open)
        }, transitions);

        await channel.DisposeAsync();
    }

    /// <summary>
    /// 查找不存在的通道时应列出已注册名称，方便用户对照拼写。
    /// </summary>
    [Fact]
    public async Task ChannelRegistry_MissingName_ListsAvailableChannels()
    {
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("meter"));
        var error = Assert.Throws<ZeusException>(() => host.Channels.Get("oven"));
        Assert.Contains("meter", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("oven", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 未启动时写入必须失败，并提示先调用 StartAsync。
    /// </summary>
    [Fact]
    public async Task Write_BeforeStart_ThrowsActionableError()
    {
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("meter"));
        var channel = host.Channels.Get("meter");
        var error = await Assert.ThrowsAsync<ZeusChannelException>(() => channel.WriteAsync(new byte[] { 0x01 }));
        Assert.Contains("StartAsync", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 重复通道名必须在构建期失败，而不是拖到运行期。
    /// </summary>
    [Fact]
    public void DuplicateChannelName_FailsAtBuild()
    {
        var error = Assert.Throws<ZeusException>(() =>
            ZeusHost.Create(builder =>
            {
                builder.AddVirtualChannel("meter");
                builder.AddVirtualChannel("meter");
            }));
        Assert.Contains("meter", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
