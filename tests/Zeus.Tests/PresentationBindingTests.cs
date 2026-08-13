using System.Text;
using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证界面绑定核心：格式化、封送、退订与宿主附件的幂等启停。
/// 使用立即调度器，不依赖 WinForms / WPF 消息循环。
/// </summary>
public sealed class PresentationBindingTests
{
    /// <summary>
    /// 可打印 ASCII 应原样展示，二进制应转为十六进制。
    /// </summary>
    [Fact]
    public void ChannelTextFormatter_ChoosesAsciiOrHex()
    {
        Assert.Equal("PING", ChannelTextFormatter.Default(Encoding.ASCII.GetBytes("PING")));
        Assert.Equal("0102", ChannelTextFormatter.Default(new byte[] { 0x01, 0x02 }));
        Assert.Equal("0102", ChannelTextFormatter.Hex(new byte[] { 0x01, 0x02 }));
        Assert.Equal(string.Empty, ChannelTextFormatter.Default(ReadOnlyMemory<byte>.Empty));
    }

    /// <summary>
    /// BindText 必须把写入回显推到 setText，释放后不再更新。
    /// </summary>
    [Fact]
    public async Task BindText_UpdatesAndUnsubscribes()
    {
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("meter"));
        var channel = host.Channels.Get("meter");
        var texts = new List<string>();
        var binding = channel.BindText(ImmediateUiDispatcher.Instance, texts.Add);

        await host.StartAsync();
        await channel.WriteAsync(Encoding.ASCII.GetBytes("PING"));
        Assert.Equal(new[] { "PING" }, texts);

        binding.Dispose();
        await channel.WriteAsync(Encoding.ASCII.GetBytes("PONG"));
        Assert.Equal(new[] { "PING" }, texts);
    }

    /// <summary>
    /// BindState 订阅时应立即推送当前状态，随后跟随状态机。
    /// </summary>
    [Fact]
    public async Task BindState_PushesCurrentAndSubsequentStates()
    {
        var channel = new VirtualChannel("meter");
        var states = new List<string>();
        using var binding = channel.BindState(ImmediateUiDispatcher.Instance, states.Add);

        Assert.Equal(new[] { nameof(ChannelState.Created) }, states);

        await channel.OpenAsync();
        Assert.Contains(nameof(ChannelState.Opening), states);
        Assert.Equal(nameof(ChannelState.Open), states[^1]);

        await channel.DisposeAsync();
    }

    /// <summary>
    /// 绑定源应在调度器上更新 LastText 与 ReceivedCount。
    /// </summary>
    [Fact]
    public async Task BindingSource_ProjectsPayloadOnDispatcher()
    {
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("meter"));
        var channel = host.Channels.Get("meter");
        using var source = channel.AsBindingSource(ImmediateUiDispatcher.Instance);
        var names = new List<string>();
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                names.Add(e.PropertyName);
            }
        };

        await host.StartAsync();
        await channel.WriteAsync(new byte[] { 0x0A, 0x0B });

        Assert.Equal(1, source.ReceivedCount);
        Assert.Equal("0A0B", source.LastHex);
        Assert.Contains(nameof(ChannelBindingSource.LastText), names);
        Assert.Contains(nameof(ChannelBindingSource.ReceivedCount), names);
    }

    /// <summary>
    /// 宿主附件的 Start/Dispose 必须幂等，避免窗口重复 Loaded/Closed 时二次打开端口。
    /// </summary>
    [Fact]
    public async Task UiHostAttachment_StartAndDisposeAreIdempotent()
    {
        var host = ZeusHost.Create(builder => builder.AddVirtualChannel("meter"));
        var attachment = new UiHostAttachment(host);

        await attachment.StartAsync();
        await attachment.StartAsync();
        Assert.Equal(ChannelState.Open, host.Channels.Get("meter").State);

        await attachment.DisposeAsync();
        await attachment.DisposeAsync();
        Assert.Equal(ChannelState.Closed, host.Channels.Get("meter").State);
    }

    /// <summary>
    /// 记录调度器调用次数，确认非当前线程访问时会走 Post。
    /// </summary>
    [Fact]
    public async Task BindText_PostsWhenDispatcherDoesNotHaveAccess()
    {
        var dispatcher = new RecordingDispatcher { HasAccess = false };
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("meter"));
        var channel = host.Channels.Get("meter");
        string? text = null;
        using var binding = channel.BindText(dispatcher, value => text = value);

        await host.StartAsync();
        await channel.WriteAsync(Encoding.ASCII.GetBytes("HI"));

        Assert.Equal(1, dispatcher.PostCount);
        Assert.Equal("HI", text);
    }

    /// <summary>
    /// 测试用调度器：可切换是否拥有界面线程，并统计 Post 次数。
    /// </summary>
    private sealed class RecordingDispatcher : IUiDispatcher
    {
        public bool HasAccess { get; set; }

        public int PostCount { get; private set; }

        public bool CheckAccess() => HasAccess;

        public void Post(Action action)
        {
            PostCount++;
            action();
        }
    }
}
