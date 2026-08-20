using System.Globalization;
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
    /// BindEnabled 默认仅在通道打开后启用，关闭后再禁用。
    /// </summary>
    [Fact]
    public async Task BindEnabled_FollowsOpenState()
    {
        var channel = new VirtualChannel("meter");
        var values = new List<bool>();
        using var binding = channel.BindEnabled(ImmediateUiDispatcher.Instance, values.Add);

        Assert.Equal(new[] { false }, values);

        await channel.OpenAsync();
        await channel.CloseAsync();

        Assert.Equal(new[] { false, false, true, false }, values);
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
    /// BindSnapshot 应立即推送当前快照，并在点值变化时继续推送完整状态。
    /// </summary>
    [Fact]
    public void BindSnapshot_PushesCurrentAndChangedSnapshot()
    {
        var table = new PointTable();
        table.Register(new PointDefinition(
            "temperature",
            "oven",
            PointValueKind.Double,
            new PointAlarmLimits(low: 0, high: 100),
            writable: true));

        var snapshots = new List<PointSnapshot>();
        using var binding = table.BindSnapshot("temperature", ImmediateUiDispatcher.Instance, snapshots.Add);

        Assert.Single(snapshots);
        Assert.Null(snapshots[0].Value);
        Assert.Equal(PointAlarmState.Unknown, snapshots[0].AlarmState);

        table.Publish("oven.temperature", 42d);

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(42d, snapshots[^1].Value);
        Assert.Equal(PointAlarmState.Normal, snapshots[^1].AlarmState);
    }

    /// <summary>
    /// PointBindingSource 应投影值、错误、报警与可写状态，并在释放后停止更新。
    /// </summary>
    [Fact]
    public void PointBindingSource_ProjectsSnapshotProperties()
    {
        var table = new PointTable();
        table.Register(new PointDefinition(
            "temperature",
            "oven",
            PointValueKind.Double,
            new PointAlarmLimits(high: 10),
            writable: true));

        using var source = table.AsBindingSource(
            "oven.temperature",
            ImmediateUiDispatcher.Instance,
            value => value is null
                ? string.Empty
                : Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.0", CultureInfo.InvariantCulture) + " C");
        var names = new List<string>();
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                names.Add(e.PropertyName);
            }
        };

        Assert.Equal("temperature", source.Name);
        Assert.Equal("oven.temperature", source.QualifiedName);
        Assert.True(source.Writable);
        Assert.Equal(PointAlarmState.Unknown, source.AlarmState);

        table.Publish("oven.temperature", 12d);

        Assert.Equal(12d, source.Value);
        Assert.Equal("12.0 C", source.Text);
        Assert.Equal(PointAlarmState.High, source.AlarmState);
        Assert.True(source.IsAlarmed);
        Assert.Contains(nameof(PointBindingSource.Text), names);
        Assert.Contains(nameof(PointBindingSource.AlarmState), names);

        table.PublishError("oven.temperature", "sensor offline");

        Assert.True(source.HasError);
        Assert.Equal("sensor offline", source.Text);
        Assert.Equal("sensor offline", source.Error);

        source.Dispose();
        table.Publish("oven.temperature", 8d);
        Assert.Equal("sensor offline", source.Text);
    }

    /// <summary>
    /// BindHistory 应立即推送当前历史，并且只在成功采样进入历史时继续推送。
    /// </summary>
    [Fact]
    public void BindHistory_PushesCurrentHistoryAndSuccessfulChanges()
    {
        var table = new PointTable();
        table.Register(new PointDefinition(
            "temperature",
            "oven",
            PointValueKind.Double,
            new PointAlarmLimits(high: 10),
            writable: false));
        table.Publish("oven.temperature", 8d);
        table.Publish("oven.temperature", 9d);

        var histories = new List<IReadOnlyList<PointSnapshot>>();
        using var binding = table.BindHistory(
            "temperature",
            ImmediateUiDispatcher.Instance,
            history => histories.Add(history.ToArray()));

        Assert.Single(histories);
        Assert.Equal(new object?[] { 8d, 9d }, histories[0].Select(item => item.Value));

        table.PublishError("oven.temperature", "sensor offline");
        Assert.Single(histories);

        table.Publish("oven.temperature", 12d);

        Assert.Equal(2, histories.Count);
        Assert.Equal(new object?[] { 8d, 9d, 12d }, histories[^1].Select(item => item.Value));
        Assert.Equal(PointAlarmState.High, histories[^1][^1].AlarmState);

        binding.Dispose();
        table.Publish("oven.temperature", 7d);
        Assert.Equal(2, histories.Count);
    }

    /// <summary>
    /// BindChart 应把可转成数值的历史变成时间-数值样本。
    /// </summary>
    [Fact]
    public void BindChart_ProjectsNumericSamples()
    {
        var table = new PointTable();
        table.Register(new PointDefinition("temperature", "oven", PointValueKind.Double, new PointAlarmLimits(high: 100)));
        table.Publish("oven.temperature", 12.5d);

        IReadOnlyList<PointChartSample>? samples = null;
        using var binding = table.BindChart("temperature", ImmediateUiDispatcher.Instance, value => samples = value);

        Assert.NotNull(samples);
        Assert.Single(samples!);
        Assert.Equal(12.5d, samples![0].Value, 3);
    }

    /// <summary>
    /// BindGauge 应把当前值按报警限映射到 0–1。
    /// </summary>
    [Fact]
    public void BindGauge_MapsValueToRatio()
    {
        var table = new PointTable();
        table.Register(new PointDefinition("temperature", "oven", PointValueKind.Double, new PointAlarmLimits(low: 0, high: 100)));
        table.Publish("oven.temperature", 25d);

        var ratios = new List<double>();
        using var binding = table.BindGauge("temperature", ImmediateUiDispatcher.Instance, ratios.Add);
        Assert.Equal(0.25d, ratios[^1], 3);
    }

    /// <summary>
    /// BindAlarms 应在越限时推送活动报警。
    /// </summary>
    [Fact]
    public void BindAlarms_PushesActiveRecords()
    {
        var table = new PointTable();
        var alarms = new PointAlarmTable(table);
        table.Register(new PointDefinition("temperature", "oven", PointValueKind.Double, new PointAlarmLimits(high: 10)));

        IReadOnlyList<PointAlarmRecord>? active = null;
        using var binding = alarms.BindAlarms(ImmediateUiDispatcher.Instance, records => active = records);
        table.Publish("oven.temperature", 20d);

        Assert.NotNull(active);
        Assert.Single(active!);
        Assert.Equal("oven.temperature", active![0].QualifiedName);
    }

    /// <summary>
    /// PointHistoryBindingSource 应投影历史、最新值和最新报警状态，并跟随点表历史容量裁剪。
    /// </summary>
    [Fact]
    public void PointHistoryBindingSource_ProjectsHistoryProperties()
    {
        var table = new PointTable(historyCapacity: 2);
        table.Register(new PointDefinition(
            "temperature",
            "oven",
            PointValueKind.Double,
            new PointAlarmLimits(high: 10),
            writable: false));
        table.Publish("oven.temperature", 8d);

        using var source = table.AsHistoryBindingSource("oven.temperature", ImmediateUiDispatcher.Instance);
        var names = new List<string>();
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                names.Add(e.PropertyName);
            }
        };

        Assert.Equal(1, source.Count);
        Assert.True(source.HasSamples);
        Assert.Equal(8d, source.LatestValue);
        Assert.Equal(PointAlarmState.Normal, source.LatestAlarmState);

        table.Publish("oven.temperature", 12d);

        Assert.Equal(2, source.Count);
        Assert.Equal(12d, source.LatestValue);
        Assert.Equal(PointAlarmState.High, source.LatestAlarmState);
        Assert.True(source.IsLatestAlarmed);
        Assert.Contains(nameof(PointHistoryBindingSource.History), names);
        Assert.Contains(nameof(PointHistoryBindingSource.LatestAlarmState), names);

        table.Publish("oven.temperature", 6d);

        Assert.Equal(new object?[] { 12d, 6d }, source.History.Select(item => item.Value));
        Assert.Equal(6d, source.LatestValue);

        source.Dispose();
        table.Publish("oven.temperature", 7d);
        Assert.Equal(6d, source.LatestValue);
    }

    /// <summary>
    /// 点表 BindEnabled 默认只在点可写且当前无错误时启用。
    /// </summary>
    [Fact]
    public void PointBindEnabled_DefaultsToWritableAndNoError()
    {
        var table = new PointTable();
        table.Register(new PointDefinition("setpoint", "oven", PointValueKind.Double, null, writable: true));
        var values = new List<bool>();

        using var binding = table.BindEnabled("setpoint", ImmediateUiDispatcher.Instance, values.Add);

        Assert.Equal(new[] { true }, values);

        table.PublishError("oven.setpoint", "write blocked");
        table.Publish("oven.setpoint", 75d);

        Assert.Equal(new[] { true, false, true }, values);
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
