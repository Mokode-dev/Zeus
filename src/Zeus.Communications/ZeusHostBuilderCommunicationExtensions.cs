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
}
