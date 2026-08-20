using System.Net.Sockets;
using System.Text;
using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证 TCP 服务端通道的连接、接收与写回行为。
/// </summary>
public sealed class TcpServerChannelTests
{
    /// <summary>
    /// TCP 服务端通道应接收客户端字节流，并能回复最近一个发送数据的客户端。
    /// </summary>
    [Fact]
    public async Task TcpServerChannel_ReceivesAndRepliesToLastSender()
    {
        await using var host = ZeusHost.Create(builder => builder.AddTcpServer("server", options =>
        {
            options.LocalAddress = "127.0.0.1";
            options.LocalPort = 0;
        }));
        var channel = Assert.IsType<TcpServerChannel>(host.Channels.Get("server"));
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, e) => received.TrySetResult(e.Data.ToArray());

        await host.StartAsync();
        var port = channel.LocalEndPoint?.Port ?? throw new InvalidOperationException("TCP 服务端未绑定端口。");
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        var request = Encoding.ASCII.GetBytes("PING");
        await stream.WriteAsync(request);

        Assert.Equal(request, await received.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.NotNull(channel.LastRemoteEndPoint);
        Assert.Equal(1, channel.ClientCount);

        var response = Encoding.ASCII.GetBytes("PONG");
        await channel.WriteAsync(response);

        Assert.Equal(response, await ReadExactAsync(stream, response.Length));
    }

    /// <summary>
    /// TCP 服务端通道应能向所有当前已连接客户端广播数据。
    /// </summary>
    [Fact]
    public async Task TcpServerChannel_BroadcastsToConnectedClients()
    {
        await using var host = ZeusHost.Create(builder => builder.AddTcpServer("server", options =>
        {
            options.LocalAddress = "127.0.0.1";
            options.LocalPort = 0;
        }));
        var channel = Assert.IsType<TcpServerChannel>(host.Channels.Get("server"));

        await host.StartAsync();
        var port = channel.LocalEndPoint?.Port ?? throw new InvalidOperationException("TCP 服务端未绑定端口。");
        using var first = new TcpClient();
        using var second = new TcpClient();
        await first.ConnectAsync("127.0.0.1", port);
        await second.ConnectAsync("127.0.0.1", port);
        await WaitUntilAsync(() => channel.ClientCount == 2);

        Assert.Equal(2, channel.RemoteEndPoints.Count);

        var payload = Encoding.ASCII.GetBytes("BROADCAST");
        await channel.BroadcastAsync(payload);

        Assert.Equal(payload, await ReadExactAsync(first.GetStream(), payload.Length));
        Assert.Equal(payload, await ReadExactAsync(second.GetStream(), payload.Length));
    }

    /// <summary>
    /// 服务端尚不知道最近请求方时，写入必须给出可操作错误。
    /// </summary>
    [Fact]
    public async Task TcpServerChannel_WriteBeforeReceiveThrows()
    {
        await using var host = ZeusHost.Create(builder => builder.AddTcpServer("server", options =>
        {
            options.LocalAddress = "127.0.0.1";
            options.LocalPort = 0;
        }));
        var channel = host.Channels.Get("server");

        await host.StartAsync();
        var error = await Assert.ThrowsAsync<ZeusChannelException>(() => channel.WriteAsync(new byte[] { 0x01 }));

        Assert.Contains("尚未收到", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 超过 MaxClients 的新连接应被立即断开。
    /// </summary>
    [Fact]
    public async Task TcpServerChannel_RejectsClientsBeyondMax()
    {
        await using var host = ZeusHost.Create(builder => builder.AddTcpServer("server", options =>
        {
            options.LocalAddress = "127.0.0.1";
            options.LocalPort = 0;
            options.MaxClients = 1;
        }));
        var channel = Assert.IsType<TcpServerChannel>(host.Channels.Get("server"));
        await host.StartAsync();
        var port = channel.LocalEndPoint?.Port ?? throw new InvalidOperationException("TCP 服务端未绑定端口。");

        using var first = new TcpClient();
        await first.ConnectAsync("127.0.0.1", port);
        await WaitUntilAsync(() => channel.ClientCount == 1);

        using var second = new TcpClient();
        await second.ConnectAsync("127.0.0.1", port);
        await Task.Delay(100);
        Assert.Equal(1, channel.ClientCount);
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), timeout.Token);
            if (read == 0)
            {
                throw new InvalidOperationException("TCP 客户端连接已关闭。");
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("等待 TCP 服务端状态更新超时。");
    }
}
