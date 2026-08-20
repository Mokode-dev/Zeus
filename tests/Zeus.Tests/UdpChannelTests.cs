using System.Net;
using System.Net.Sockets;
using System.Text;
using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证 UDP 客户端通道的数据报收发行为。
/// </summary>
public sealed class UdpChannelTests
{
    /// <summary>
    /// UDP 通道应能向对端发送数据报，并把收到的回包发布为 DataReceived。
    /// </summary>
    [Fact]
    public async Task UdpClientChannel_SendsAndReceivesDatagrams()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var echoLoop = EchoOneDatagramAsync(server, serverCts.Token);

        await using var host = ZeusHost.Create(builder => builder.AddUdpClient("udp", "127.0.0.1", serverPort));
        var channel = host.Channels.Get("udp");
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, e) => received.TrySetResult(e.Data.ToArray());

        await host.StartAsync();
        Assert.Equal(ChannelState.Open, channel.State);

        var payload = Encoding.ASCII.GetBytes("PING");
        await channel.WriteAsync(payload);

        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(payload, actual);
        await echoLoop;
    }

    /// <summary>
    /// UDP 服务端通道应接收客户端数据报，并能回复最近一个发送方。
    /// </summary>
    [Fact]
    public async Task UdpServerChannel_ReceivesAndRepliesToLastSender()
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await using var host = ZeusHost.Create(builder => builder.AddUdpServer("server", options =>
        {
            options.LocalAddress = "127.0.0.1";
            options.LocalPort = 0;
        }));
        var channel = Assert.IsType<UdpServerChannel>(host.Channels.Get("server"));
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, e) => received.TrySetResult(e.Data.ToArray());

        await host.StartAsync();
        var port = channel.LocalEndPoint?.Port ?? throw new InvalidOperationException("UDP 服务端未绑定本地端口。");
        var request = Encoding.ASCII.GetBytes("PING");
        await client.SendAsync(request, new IPEndPoint(IPAddress.Loopback, port));

        Assert.Equal(request, await received.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.NotNull(channel.LastRemoteEndPoint);

        var response = Encoding.ASCII.GetBytes("PONG");
        await channel.WriteAsync(response);
        var actual = await client.ReceiveAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
        Assert.Equal(response, actual.Buffer);
    }

    /// <summary>
    /// 带远端的 WriteAsync 应能回复指定发送方，而不是最近一个。
    /// </summary>
    [Fact]
    public async Task UdpServerChannel_WritesToSpecifiedRemote()
    {
        using var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var second = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await using var host = ZeusHost.Create(builder => builder.AddUdpServer("server", options =>
        {
            options.LocalAddress = "127.0.0.1";
            options.LocalPort = 0;
        }));
        var channel = Assert.IsType<UdpServerChannel>(host.Channels.Get("server"));
        var firstReceived = new TaskCompletionSource<System.Net.EndPoint?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReceived = new TaskCompletionSource<System.Net.EndPoint?>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.DataReceived += (_, e) =>
        {
            if (e.Data.Span.SequenceEqual("ONE"u8))
            {
                firstReceived.TrySetResult(e.RemoteEndPoint);
            }
            else if (e.Data.Span.SequenceEqual("TWO"u8))
            {
                secondReceived.TrySetResult(e.RemoteEndPoint);
            }
        };

        await host.StartAsync();
        var port = channel.LocalEndPoint?.Port ?? throw new InvalidOperationException("UDP 服务端未绑定本地端口。");
        var server = new IPEndPoint(IPAddress.Loopback, port);
        await first.SendAsync("ONE"u8.ToArray(), server);
        var firstRemote = await firstReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await second.SendAsync("TWO"u8.ToArray(), server);
        await secondReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.NotNull(firstRemote);
        await channel.WriteAsync(firstRemote!, "PONG"u8.ToArray());
        var actual = await first.ReceiveAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
        Assert.Equal("PONG"u8.ToArray(), actual.Buffer);
    }

    /// <summary>
    /// 服务端尚不知道远端时，写入必须给出可操作错误。
    /// </summary>
    [Fact]
    public async Task UdpServerChannel_WriteBeforeReceiveThrows()
    {
        await using var host = ZeusHost.Create(builder => builder.AddUdpServer("server", options =>
        {
            options.LocalAddress = "127.0.0.1";
            options.LocalPort = 0;
        }));
        var channel = host.Channels.Get("server");

        await host.StartAsync();
        var error = await Assert.ThrowsAsync<ZeusChannelException>(() => channel.WriteAsync(new byte[] { 0x01 }));
        Assert.Contains("尚未收到", error.Message, StringComparison.Ordinal);
    }

    private static async Task EchoOneDatagramAsync(UdpClient server, CancellationToken cancellationToken)
    {
        var request = await server.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        await server.SendAsync(request.Buffer, request.Buffer.Length, request.RemoteEndPoint).ConfigureAwait(false);
    }

}
