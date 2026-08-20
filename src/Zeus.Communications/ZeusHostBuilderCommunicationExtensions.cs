using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 向 <see cref="ZeusHostBuilder"/> 注册传输通道的扩展方法。
/// 这是用户面对的「神谕层」入口：一行声明通道，不必自己 new 具体类型。
/// </summary>
public static class ZeusHostBuilderCommunicationExtensions
{
    /// <summary>
    /// 注册真实串口通道。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名，后续 <c>Channels.Get</c> 使用。</param>
    /// <param name="portName">操作系统端口名，例如 <c>COM3</c>。</param>
    /// <param name="baudRate">波特率，默认 115200。</param>
    /// <returns>同一构建器，便于链式调用。</returns>
    public static ZeusHostBuilder AddSerialPort(
        this ZeusHostBuilder builder,
        string name,
        string portName,
        int baudRate = 115200)
    {
        return builder.AddSerialPort(name, options =>
        {
            options.PortName = portName;
            options.BaudRate = baudRate;
        });
    }

    /// <summary>
    /// 以选项回调注册串口通道，用于需要校验位或超时的场景。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名。</param>
    /// <param name="configure">配置串口参数。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddSerialPort(
        this ZeusHostBuilder builder,
        string name,
        Action<SerialPortOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SerialPortOptions();
        configure(options);

        builder.Register((services, channels, _) =>
        {
            var logger = services.GetService<ILogger<SerialPortChannel>>();
            channels.Add(new SerialPortChannel(name, options, logger));
        });

        return builder;
    }

    /// <summary>
    /// 注册内存虚拟通道。写入即回显，适合无硬件时的示例与测试。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名。</param>
    /// <param name="responder">可选对端。为 <c>null</c> 时回显写入内容；传入 <see cref="IVirtualResponder"/> 可模拟从站。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddVirtualChannel(
        this ZeusHostBuilder builder,
        string name,
        IVirtualResponder? responder = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Register((services, channels, _) =>
        {
            var logger = services.GetService<ILogger<VirtualChannel>>();
            channels.Add(new VirtualChannel(name, logger, responder));
        });
        return builder;
    }

    /// <summary>
    /// 注册 TCP 客户端通道。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名。</param>
    /// <param name="host">对端主机名或 IP。</param>
    /// <param name="port">对端端口。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddTcpClient(this ZeusHostBuilder builder, string name, string host, int port)
    {
        return builder.AddTcpClient(name, options =>
        {
            options.Host = host;
            options.Port = port;
        });
    }

    /// <summary>
    /// 以选项回调注册 TCP 客户端。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名。</param>
    /// <param name="configure">配置连接参数。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddTcpClient(
        this ZeusHostBuilder builder,
        string name,
        Action<TcpClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TcpClientOptions();
        configure(options);

        builder.Register((services, channels, _) =>
        {
            var logger = services.GetService<ILogger<TcpClientChannel>>();
            channels.Add(new TcpClientChannel(name, options, logger));
        });

        return builder;
    }

    /// <summary>
    /// 注册 TCP 服务端通道。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名。</param>
    /// <param name="localPort">本地监听端口。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddTcpServer(this ZeusHostBuilder builder, string name, int localPort)
    {
        return builder.AddTcpServer(name, options => options.LocalPort = localPort);
    }

    /// <summary>
    /// 以选项回调注册 TCP 服务端。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名。</param>
    /// <param name="configure">配置本地监听参数。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddTcpServer(
        this ZeusHostBuilder builder,
        string name,
        Action<TcpServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TcpServerOptions();
        configure(options);

        builder.Register((services, channels, _) =>
        {
            var logger = services.GetService<ILogger<TcpServerChannel>>();
            channels.Add(new TcpServerChannel(name, options, logger));
        });

        return builder;
    }

    /// <summary>
    /// 注册 UDP 客户端通道。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名。</param>
    /// <param name="host">对端主机名或 IP。</param>
    /// <param name="port">对端端口。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddUdpClient(this ZeusHostBuilder builder, string name, string host, int port)
    {
        return builder.AddUdpClient(name, options =>
        {
            options.Host = host;
            options.Port = port;
        });
    }

    /// <summary>
    /// 以选项回调注册 UDP 客户端。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名。</param>
    /// <param name="configure">配置端点与本地端口。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddUdpClient(
        this ZeusHostBuilder builder,
        string name,
        Action<UdpClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new UdpClientOptions();
        configure(options);

        builder.Register((services, channels, _) =>
        {
            var logger = services.GetService<ILogger<UdpClientChannel>>();
            channels.Add(new UdpClientChannel(name, options, logger));
        });

        return builder;
    }

    /// <summary>
    /// 注册 UDP 服务端通道。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名。</param>
    /// <param name="localPort">本地监听端口。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddUdpServer(this ZeusHostBuilder builder, string name, int localPort)
    {
        return builder.AddUdpServer(name, options => options.LocalPort = localPort);
    }

    /// <summary>
    /// 以选项回调注册 UDP 服务端。
    /// </summary>
    /// <param name="builder">宿主构建器。</param>
    /// <param name="name">通道名。</param>
    /// <param name="configure">配置本地监听参数。</param>
    /// <returns>同一构建器。</returns>
    public static ZeusHostBuilder AddUdpServer(
        this ZeusHostBuilder builder,
        string name,
        Action<UdpServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new UdpServerOptions();
        configure(options);

        builder.Register((services, channels, _) =>
        {
            var logger = services.GetService<ILogger<UdpServerChannel>>();
            channels.Add(new UdpServerChannel(name, options, logger));
        });

        return builder;
    }

    /// <summary>
    /// 在已构建的宿主上登记串口通道。宿主运行中会立即打开。
    /// </summary>
    public static Task<SerialPortChannel> AddSerialPortAsync(
        this IZeusHost host,
        string name,
        string portName,
        int baudRate = 115200,
        CancellationToken cancellationToken = default)
        => host.AddSerialPortAsync(name, options =>
        {
            options.PortName = portName;
            options.BaudRate = baudRate;
        }, cancellationToken);

    /// <summary>
    /// 以选项回调在已构建的宿主上登记串口通道。
    /// </summary>
    public static Task<SerialPortChannel> AddSerialPortAsync(
        this IZeusHost host,
        string name,
        Action<SerialPortOptions> configure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new SerialPortOptions();
        configure(options);
        var logger = host.Services.GetService<ILogger<SerialPortChannel>>();
        return AddAndMaybeOpenAsync(host, new SerialPortChannel(name, options, logger), cancellationToken);
    }

    /// <summary>
    /// 在已构建的宿主上登记虚拟通道。宿主运行中会立即打开。
    /// </summary>
    public static Task<VirtualChannel> AddVirtualChannelAsync(
        this IZeusHost host,
        string name,
        IVirtualResponder? responder = null,
        CancellationToken cancellationToken = default)
    {
        var logger = host.Services.GetService<ILogger<VirtualChannel>>();
        return AddAndMaybeOpenAsync(host, new VirtualChannel(name, logger, responder), cancellationToken);
    }

    /// <summary>
    /// 在已构建的宿主上登记 TCP 客户端通道。
    /// </summary>
    public static Task<TcpClientChannel> AddTcpClientAsync(
        this IZeusHost host,
        string name,
        string hostName,
        int port,
        CancellationToken cancellationToken = default)
        => host.AddTcpClientAsync(name, options =>
        {
            options.Host = hostName;
            options.Port = port;
        }, cancellationToken);

    /// <summary>
    /// 以选项回调在已构建的宿主上登记 TCP 客户端通道。
    /// </summary>
    public static Task<TcpClientChannel> AddTcpClientAsync(
        this IZeusHost host,
        string name,
        Action<TcpClientOptions> configure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TcpClientOptions();
        configure(options);
        var logger = host.Services.GetService<ILogger<TcpClientChannel>>();
        return AddAndMaybeOpenAsync(host, new TcpClientChannel(name, options, logger), cancellationToken);
    }

    /// <summary>
    /// 在已构建的宿主上登记 TCP 服务端通道。
    /// </summary>
    public static Task<TcpServerChannel> AddTcpServerAsync(
        this IZeusHost host,
        string name,
        int localPort,
        CancellationToken cancellationToken = default)
        => host.AddTcpServerAsync(name, options => options.LocalPort = localPort, cancellationToken);

    /// <summary>
    /// 以选项回调在已构建的宿主上登记 TCP 服务端通道。
    /// </summary>
    public static Task<TcpServerChannel> AddTcpServerAsync(
        this IZeusHost host,
        string name,
        Action<TcpServerOptions> configure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TcpServerOptions();
        configure(options);
        var logger = host.Services.GetService<ILogger<TcpServerChannel>>();
        return AddAndMaybeOpenAsync(host, new TcpServerChannel(name, options, logger), cancellationToken);
    }

    /// <summary>
    /// 在已构建的宿主上登记 UDP 客户端通道。
    /// </summary>
    public static Task<UdpClientChannel> AddUdpClientAsync(
        this IZeusHost host,
        string name,
        string hostName,
        int port,
        CancellationToken cancellationToken = default)
        => host.AddUdpClientAsync(name, options =>
        {
            options.Host = hostName;
            options.Port = port;
        }, cancellationToken);

    /// <summary>
    /// 以选项回调在已构建的宿主上登记 UDP 客户端通道。
    /// </summary>
    public static Task<UdpClientChannel> AddUdpClientAsync(
        this IZeusHost host,
        string name,
        Action<UdpClientOptions> configure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new UdpClientOptions();
        configure(options);
        var logger = host.Services.GetService<ILogger<UdpClientChannel>>();
        return AddAndMaybeOpenAsync(host, new UdpClientChannel(name, options, logger), cancellationToken);
    }

    /// <summary>
    /// 在已构建的宿主上登记 UDP 服务端通道。
    /// </summary>
    public static Task<UdpServerChannel> AddUdpServerAsync(
        this IZeusHost host,
        string name,
        int localPort,
        CancellationToken cancellationToken = default)
        => host.AddUdpServerAsync(name, options => options.LocalPort = localPort, cancellationToken);

    /// <summary>
    /// 以选项回调在已构建的宿主上登记 UDP 服务端通道。
    /// </summary>
    public static Task<UdpServerChannel> AddUdpServerAsync(
        this IZeusHost host,
        string name,
        Action<UdpServerOptions> configure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new UdpServerOptions();
        configure(options);
        var logger = host.Services.GetService<ILogger<UdpServerChannel>>();
        return AddAndMaybeOpenAsync(host, new UdpServerChannel(name, options, logger), cancellationToken);
    }

    /// <summary>
    /// 登记通道；宿主已启动时立即打开，失败记入 <see cref="ChannelState.Faulted"/> 并由自动重连接管。
    /// </summary>
    private static async Task<TChannel> AddAndMaybeOpenAsync<TChannel>(
        IZeusHost host,
        TChannel channel,
        CancellationToken cancellationToken)
        where TChannel : IChannel
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Channels.Add(channel);
        if (!host.IsRunning)
        {
            return channel;
        }

        try
        {
            await channel.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 打开失败已进入 Faulted；自动重连服务会按退避重试。
            // 调用方仍拿到通道实例，可通过 State 判断是否需要立即处理。
        }

        return channel;
    }
}
