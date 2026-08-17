using System.Net.Sockets;
using System.Text;
using Zeus;

// 本机回环演示：Zeus 监听 TCP 端口，两个客户端连接后完成请求回复和广播。
await using var app = ZeusHost.Create(builder => builder.AddTcpServer("server", options =>
{
    options.LocalAddress = "127.0.0.1";
    options.LocalPort = 0;
}));

var server = (TcpServerChannel)app.Channels.Get("server");
using var trace = new ChannelTraceBuffer(server, capacity: 16);
var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

server.DataReceived += (_, e) =>
{
    var text = Encoding.ASCII.GetString(e.Data.Span);
    Console.WriteLine($"服务端收到：{text}");
    received.TrySetResult(text);
};

await app.StartAsync();
var port = server.LocalEndPoint?.Port ?? throw new InvalidOperationException("TCP 服务端未绑定端口。");
Console.WriteLine($"TCP 服务端已监听 127.0.0.1:{port}");

using var first = new TcpClient();
using var second = new TcpClient();
await first.ConnectAsync("127.0.0.1", port);
await second.ConnectAsync("127.0.0.1", port);
await WaitUntilAsync(() => server.ClientCount == 2);
Console.WriteLine($"已连接客户端：{server.ClientCount}");

var firstStream = first.GetStream();
var secondStream = second.GetStream();
await firstStream.WriteAsync(Encoding.ASCII.GetBytes("PING"));

var request = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
await server.WriteAsync(Encoding.ASCII.GetBytes($"ACK:{request}"));
Console.WriteLine($"客户端 1 收到回复：{Encoding.ASCII.GetString(await ReadExactAsync(firstStream, 8))}");

await server.BroadcastAsync(Encoding.ASCII.GetBytes("BROADCAST"));
Console.WriteLine($"客户端 1 收到广播：{Encoding.ASCII.GetString(await ReadExactAsync(firstStream, 9))}");
Console.WriteLine($"客户端 2 收到广播：{Encoding.ASCII.GetString(await ReadExactAsync(secondStream, 9))}");

Console.WriteLine("最近报文追踪：");
foreach (var entry in trace.Entries)
{
    Console.WriteLine($"- {entry.Direction}: {BitConverter.ToString(entry.Data.ToArray())}");
}

await app.StopAsync();

static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length)
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

static async Task WaitUntilAsync(Func<bool> condition)
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
