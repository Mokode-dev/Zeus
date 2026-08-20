using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证 0.17 宿主日志构建面、通信报文日志、协议采集失败日志与 EventId。
/// </summary>
public sealed class HostLoggingTests
{
    /// <summary>
    /// 构建器必须暴露 Logging / Configuration / Environment，便于接入标准 MEL 管线。
    /// </summary>
    [Fact]
    public async Task HostBuilder_ExposesLoggingConfigurationAndEnvironment()
    {
        await using var host = ZeusHost.Create(builder =>
        {
            Assert.NotNull(builder.Logging);
            Assert.NotNull(builder.Configuration);
            Assert.NotNull(builder.Environment);
            builder.Logging.ClearProviders();
            builder.AddVirtualChannel("loop");
        });

        Assert.NotNull(host.Services.GetRequiredService<ILoggerFactory>());
        Assert.NotNull(host.Services.GetRequiredService<ILogger<ZeusHostBuilder>>());
    }

    /// <summary>
    /// AddCommunicationLogging 应把 TX/RX 写成带 PacketTrace EventId 的结构化日志。
    /// </summary>
    [Fact]
    public async Task AddCommunicationLogging_WritesPacketTraceWithEventId()
    {
        var provider = new CapturingLoggerProvider();
        await using var host = ZeusHost.Create(builder =>
        {
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
            builder.Logging.AddProvider(provider);
            builder.AddVirtualChannel("loop");
            builder.AddCommunicationLogging(LogLevel.Debug);
        });

        var channel = host.Channels.Get("loop");
        await host.StartAsync();
        await channel.WriteAsync("PING"u8.ToArray());

        var traces = provider.Logger.Entries.Where(entry => entry.EventId == ZeusLogEvents.PacketTrace).ToArray();
        Assert.True(traces.Length >= 2);
        Assert.Contains(traces, entry => entry.Message.Contains("50494E47", StringComparison.Ordinal));
        Assert.Contains(traces, entry => entry.Message.Contains(nameof(ChannelTraceDirection.Sent), StringComparison.Ordinal));
        Assert.Contains(traces, entry => entry.Message.Contains(nameof(ChannelTraceDirection.Received), StringComparison.Ordinal));
    }

    /// <summary>
    /// 协议设备采集失败必须打 AcquisitionFailed，并带上设备名。
    /// </summary>
    [Fact]
    public async Task ProtocolDevice_LogsAcquisitionFailure()
    {
        var provider = new CapturingLoggerProvider();
        var memory = new ModbusSlaveMemory();
        await using var host = ZeusHost.Create(builder =>
        {
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
            builder.Logging.AddProvider(provider);
            builder.AddAcquisition(TimeSpan.FromMilliseconds(40));
            builder.AddVirtualChannel("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
            builder.AddModbusRtu("oven", "bus", unitId: 1, timeout: TimeSpan.FromMilliseconds(30), points: map =>
            {
                map.HoldingRegister("temperature", 0);
            });
        });

        await host.StartAsync();
        await host.Channels.Get("bus").CloseAsync();
        await Task.Delay(200);

        Assert.Contains(
            provider.Logger.Entries,
            entry => entry.EventId == ZeusLogEvents.AcquisitionFailed
                && entry.Message.Contains("oven", StringComparison.Ordinal));
    }

    /// <summary>
    /// 通道打开失败必须使用 ChannelOpenFailed EventId，宿主仍能启动。
    /// </summary>
    [Fact]
    public async Task Host_LogsChannelOpenFailedWithEventId()
    {
        var provider = new CapturingLoggerProvider();
        await using var host = ZeusHost.Create(builder =>
        {
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
            builder.Logging.AddProvider(provider);
            builder.AddReconnect(options => options.Enabled = false);
            builder.Register((_, channels, _) => channels.Add(new FailingOpenChannel("broken")));
        });

        await host.StartAsync();
        Assert.True(host.IsRunning);
        Assert.Contains(provider.Logger.Entries, entry => entry.EventId == ZeusLogEvents.ChannelOpenFailed);
    }

    /// <summary>
    /// 捕获测试日志。所有分类共用同一记录器，便于按 EventId 断言。
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public CapturingLogger Logger { get; } = new();

        public ILogger CreateLogger(string categoryName) => Logger;

        public void Dispose()
        {
        }
    }

    /// <summary>记录级别、事件编号与格式化消息。</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, eventId, formatter(state, exception)));
        }
    }

    /// <summary>打开时抛错，用于验证启动日志。</summary>
    private sealed class FailingOpenChannel : ChannelBase
    {
        public FailingOpenChannel(string name)
            : base(name, null)
        {
        }

        protected override Task OpenCoreAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("测试用打开失败。");

        protected override Task CloseCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
