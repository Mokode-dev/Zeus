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

    private static async Task EchoOneDatagramAsync(UdpClient server, CancellationToken cancellationToken)
    {
        var request = await server.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        await server.SendAsync(request.Buffer, request.Buffer.Length, request.RemoteEndPoint).ConfigureAwait(false);
    }
}
