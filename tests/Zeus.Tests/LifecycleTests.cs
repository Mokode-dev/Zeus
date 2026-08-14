using Microsoft.Extensions.DependencyInjection;
using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证 0.2 生命周期：宿主可再次启动、故障后可重开、自动重连，以及运行中增删通道与设备。
/// </summary>
public sealed class LifecycleTests
{
    /// <summary>
    /// 停止后再启动必须重新打开通道，并允许再次写入。
    /// </summary>
    [Fact]
    public async Task Host_CanRestartAfterStop()
    {
        await using var host = ZeusHost.Create(builder => builder.AddVirtualChannel("loop"));
        var channel = host.Channels.Get("loop");

        await host.StartAsync();
        Assert.True(host.IsRunning);
        Assert.Equal(ChannelState.Open, channel.State);

        await host.StopAsync();
        Assert.False(host.IsRunning);
        Assert.Equal(ChannelState.Closed, channel.State);

        await host.StartAsync();
        Assert.True(host.IsRunning);
        Assert.Equal(ChannelState.Open, channel.State);
        await channel.WriteAsync(new byte[] { 0x01 });
    }

    /// <summary>
    /// 对已关闭或故障的通道再次 OpenAsync 必须先清理再打开，而不是要求重建实例。
    /// </summary>
    [Fact]
    public async Task Channel_OpenAsync_RecoversFromClosedAndFaulted()
    {
        var channel = new RecoverableChannel("bus");
        await channel.OpenAsync();
        await channel.CloseAsync();
        Assert.Equal(ChannelState.Closed, channel.State);

        await channel.OpenAsync();
        Assert.Equal(ChannelState.Open, channel.State);
        Assert.Equal(2, channel.OpenCount);
        Assert.True(channel.CloseCount >= 1);

        channel.FailNextWrite = true;
        var writeError = await Assert.ThrowsAsync<ZeusChannelException>(() => channel.WriteAsync(new byte[] { 0x01 }));
        Assert.Equal(ChannelState.Faulted, channel.State);
        Assert.Contains("写入失败", writeError.Message, StringComparison.Ordinal);

        await channel.OpenAsync();
        Assert.Equal(ChannelState.Open, channel.State);
        await channel.WriteAsync(new byte[] { 0x02 });
        await channel.DisposeAsync();
    }

    /// <summary>
    /// 通道进入 Faulted 后，自动重连应在短延迟内再次打开。
    /// </summary>
    [Fact]
    public async Task Host_AutomaticallyReopensFaultedChannel()
    {
        var channel = new RecoverableChannel("bus");
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddReconnect(options =>
            {
                options.Enabled = true;
                options.InitialDelay = TimeSpan.FromMilliseconds(40);
                options.MaxDelay = TimeSpan.FromMilliseconds(40);
            });
            builder.Register((_, channels, _) => channels.Add(channel));
        });

        await host.StartAsync();
        channel.FailNextWrite = true;
        await Assert.ThrowsAsync<ZeusChannelException>(() => channel.WriteAsync(new byte[] { 0x01 }));
        Assert.Equal(ChannelState.Faulted, channel.State);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && channel.State != ChannelState.Open)
        {
            await Task.Delay(20);
        }

        Assert.Equal(ChannelState.Open, channel.State);
        await channel.WriteAsync(new byte[] { 0x02 });
    }

    /// <summary>
    /// 自动重连第一次打开失败后，必须继续退避而不是停在 Faulted。
    /// </summary>
    [Fact]
    public async Task Host_RetriesReconnectAfterFailedOpen()
    {
        var channel = new RecoverableChannel("bus");
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddReconnect(options =>
            {
                options.Enabled = true;
                options.InitialDelay = TimeSpan.FromMilliseconds(30);
                options.MaxDelay = TimeSpan.FromMilliseconds(30);
            });
            builder.Register((_, channels, _) => channels.Add(channel));
        });

        await host.StartAsync();
        Assert.Equal(1, channel.OpenCount);
        channel.FailOpenTimes = 1;
        channel.FailNextWrite = true;
        await Assert.ThrowsAsync<ZeusChannelException>(() => channel.WriteAsync(new byte[] { 0x01 }));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && channel.State != ChannelState.Open)
        {
            await Task.Delay(20);
        }

        Assert.Equal(ChannelState.Open, channel.State);
        Assert.True(channel.OpenCount >= 3);
    }

    /// <summary>
    /// 关闭自动重连后，故障通道必须保持 Faulted，直到调用方自行 OpenAsync。
    /// </summary>
    [Fact]
    public async Task Host_DoesNotReconnectWhenDisabled()
    {
        var channel = new RecoverableChannel("bus");
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddReconnect(options => options.Enabled = false);
            builder.Register((_, channels, _) => channels.Add(channel));
        });

        await host.StartAsync();
        channel.FailNextWrite = true;
        await Assert.ThrowsAsync<ZeusChannelException>(() => channel.WriteAsync(new byte[] { 0x01 }));
        await Task.Delay(120);
        Assert.Equal(ChannelState.Faulted, channel.State);
    }

    /// <summary>
    /// 运行中增删虚拟通道与 Modbus 设备后，点表应随之增减。
    /// </summary>
    [Fact]
    public async Task Host_CanAddAndRemoveChannelAndDeviceAtRuntime()
    {
        var memory = new ModbusSlaveMemory();
        memory.HoldingRegisters[0] = 42;

        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddAcquisition(TimeSpan.FromMilliseconds(80));
            builder.AddVirtualChannel("idle");
        });

        await host.StartAsync();
        Assert.Empty(host.Points.All);

        await host.AddVirtualChannelAsync("bus", new ModbusSlaveResponder(1, ModbusTransport.Rtu, memory));
        host.AddModbusRtu("oven", "bus", points: map => map.HoldingRegister("pv", 0));

        var value = await WaitForPointAsync<ushort>(host, "pv");
        Assert.Equal((ushort)42, value);

        await host.RemoveDeviceAsync("oven");
        Assert.Empty(host.Points.All);
        Assert.Throws<ZeusException>(() => host.Devices.Get<ModbusDevice>("oven"));

        await host.RemoveChannelAsync("bus");
        Assert.False(host.Channels.TryGet("bus", out _));
        Assert.NotNull(host.Channels.Get("idle"));
    }

    /// <summary>
    /// 移除仍被设备占用的通道且不允许级联时，必须给出可操作的错误。
    /// </summary>
    [Fact]
    public async Task RemoveChannel_WithoutCascade_ThrowsWhenDeviceBound()
    {
        await using var host = ZeusHost.Create(builder =>
        {
            builder.AddVirtualChannel("bus");
            builder.AddModbusRtu("oven", "bus");
        });

        var error = await Assert.ThrowsAsync<ZeusException>(
            () => host.RemoveChannelAsync("bus", removeBoundDevices: false));
        Assert.Contains("oven", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(host.Channels.Get("bus"));
        Assert.NotNull(host.Devices.Get<ModbusDevice>("oven"));
    }

    /// <summary>
    /// ReloadAsync 应按 JSON 差异增删设备，而不只改采集间隔。
    /// </summary>
    [Fact]
    public async Task ReloadAsync_AddsAndRemovesDevices()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zeus-reload-{Guid.NewGuid():N}.json");
        const string initial = """
            {
              "acquisition": { "intervalMilliseconds": 80, "pollImmediately": true },
              "channels": [
                { "name": "bus", "type": "virtual", "responder": "modbus", "unitId": 1, "transport": "rtu" }
              ],
              "devices": [
                {
                  "name": "oven",
                  "channel": "bus",
                  "type": "modbus-rtu",
                  "unitId": 1,
                  "points": [ { "name": "pv", "table": "holding", "address": 0 } ]
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, initial);
        try
        {
            await using var host = ZeusHost.Create(builder => builder.AddJsonFile(path, watch: false));
            await host.StartAsync();
            Assert.NotNull(host.Devices.Get<ModbusDevice>("oven"));
            Assert.Equal((ushort)0, await WaitForPointAsync<ushort>(host, "pv"));

            var updated = """
                {
                  "acquisition": { "intervalMilliseconds": 250, "pollImmediately": true },
                  "reconnect": { "enabled": true, "initialDelayMilliseconds": 500, "maxDelayMilliseconds": 5000, "backoffMultiplier": 2 },
                  "channels": [
                    { "name": "bus", "type": "virtual", "responder": "modbus", "unitId": 1, "transport": "rtu" }
                  ],
                  "devices": [
                    {
                      "name": "dryer",
                      "channel": "bus",
                      "type": "modbus-rtu",
                      "unitId": 1,
                      "points": [ { "name": "humidity", "table": "holding", "address": 1 } ]
                    }
                  ]
                }
                """;
            await File.WriteAllTextAsync(path, updated);
            await host.ReloadAsync(path);

            Assert.Equal(TimeSpan.FromMilliseconds(250), host.Services.GetRequiredService<AcquisitionOptions>().Interval);
            Assert.False(host.Devices.TryGet<ModbusDevice>("oven", out _));
            Assert.NotNull(host.Devices.Get<ModbusDevice>("dryer"));
            Assert.Equal((ushort)0, await WaitForPointAsync<ushort>(host, "humidity"));
            Assert.Throws<ZeusException>(() => host.Points.Get("pv"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<T> WaitForPointAsync<T>(IZeusHost host, string name)
    {
        if (!host.IsRunning)
        {
            await host.StartAsync();
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (host.Points.TryGet<T>(name, out var value) && value is not null)
            {
                return value;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"等待点 {name} 超时。");
    }

    /// <summary>
    /// 测试用通道：可按需让下一次写入失败以进入 Faulted，并统计打开/关闭次数。
    /// </summary>
    private sealed class RecoverableChannel : ChannelBase
    {
        public RecoverableChannel(string name)
            : base(name, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
        {
        }

        public int OpenCount { get; private set; }

        public int CloseCount { get; private set; }

        public bool FailNextWrite { get; set; }

        /// <summary>接下来若干次 OpenCore 抛出异常，用于验证失败后仍会继续退避。</summary>
        public int FailOpenTimes { get; set; }

        protected override Task OpenCoreAsync(CancellationToken cancellationToken)
        {
            OpenCount++;
            if (FailOpenTimes > 0)
            {
                FailOpenTimes--;
                throw new IOException("模拟打开失败。");
            }

            return Task.CompletedTask;
        }

        protected override Task CloseCoreAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            return Task.CompletedTask;
        }

        protected override Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new IOException("模拟链路断开。");
            }

            return Task.CompletedTask;
        }
    }
}
