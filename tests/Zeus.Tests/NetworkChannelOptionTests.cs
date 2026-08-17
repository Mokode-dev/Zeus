using Zeus;

namespace Zeus.Tests;

/// <summary>
/// 验证 TCP/UDP 通道选项在构建期给出可操作错误。
/// </summary>
public sealed class NetworkChannelOptionTests
{
    /// <summary>
    /// 非法网络选项应在宿主构建期失败，而不是延后到底层套接字打开时。
    /// </summary>
    /// <param name="configure">待验证的宿主配置。</param>
    /// <param name="expectedFragments">错误消息中应出现的关键片段。</param>
    [Theory]
    [MemberData(nameof(InvalidNetworkOptions))]
    public void InvalidNetworkOptions_FailAtBuild(
        Action<ZeusHostBuilder> configure,
        string[] expectedFragments)
    {
        var error = Assert.Throws<ZeusChannelException>(() => ZeusHost.Create(configure));
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 非法网络选项用例。
    /// </summary>
    public static IEnumerable<object[]> InvalidNetworkOptions()
    {
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddTcpClient("tcp", " ", 502)),
            new[] { "tcp", "TCP", "主机名" }
        ];
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddTcpClient("tcp", "127.0.0.1", 0)),
            new[] { "tcp", "TCP", "1 到 65535" }
        ];
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddTcpClient("tcp", options =>
            {
                options.ConnectTimeoutMilliseconds = 0;
            })),
            new[] { "tcp", nameof(TcpClientOptions.ConnectTimeoutMilliseconds), "大于 0" }
        ];
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddTcpServer("tcp-server", options =>
            {
                options.LocalAddress = "not-an-ip";
            })),
            new[] { "tcp-server", "TCP 服务端", "本地地址" }
        ];
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddTcpServer("tcp-server", options =>
            {
                options.LocalPort = -1;
            })),
            new[] { "tcp-server", "TCP 服务端", "本地端口" }
        ];
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddTcpServer("tcp-server", options =>
            {
                options.Backlog = 0;
            })),
            new[] { "tcp-server", nameof(TcpServerOptions.Backlog), "大于 0" }
        ];
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddUdpClient("udp", options =>
            {
                options.Host = null!;
            })),
            new[] { "udp", "UDP", "主机名" }
        ];
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddUdpClient("udp", options =>
            {
                options.LocalPort = -1;
            })),
            new[] { "udp", "UDP", "本地端口" }
        ];
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddUdpClient("udp", options =>
            {
                options.ReceiveBufferSize = -1;
            })),
            new[] { "udp", nameof(UdpClientOptions.ReceiveBufferSize), "不能为负数" }
        ];
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddUdpServer("udp-server", options =>
            {
                options.LocalAddress = "not-an-ip";
            })),
            new[] { "udp-server", "UDP 服务端", "本地地址" }
        ];
        yield return
        [
            new Action<ZeusHostBuilder>(builder => builder.AddUdpServer("udp-server", options =>
            {
                options.LocalPort = -1;
            })),
            new[] { "udp-server", "UDP 服务端", "本地端口" }
        ];
    }
}
