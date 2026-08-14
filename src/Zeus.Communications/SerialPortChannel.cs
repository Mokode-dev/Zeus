using System.IO.Ports;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 基于 <see cref="SerialPort"/> 的真实串口通道。
/// 打开失败会转成带端口名与建议动作的 <see cref="ZeusChannelException"/>，避免用户直接面对 Win32 错误码。
/// </summary>
public sealed class SerialPortChannel : ChannelBase
{
    private readonly SerialPortOptions _options;
    private SerialPort? _port;

    /// <summary>
    /// 创建串口通道。
    /// </summary>
    /// <param name="name">宿主内唯一名称。</param>
    /// <param name="options">串口参数。内部会复制关键字段，避免构建后被外部改写。</param>
    /// <param name="logger">诊断日志。</param>
    public SerialPortChannel(string name, SerialPortOptions options, ILogger<SerialPortChannel>? logger = null)
        : base(name, logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = new SerialPortOptions
        {
            PortName = options.PortName,
            BaudRate = options.BaudRate,
            DataBits = options.DataBits,
            Parity = options.Parity,
            StopBits = options.StopBits,
            ReadTimeoutMilliseconds = options.ReadTimeoutMilliseconds,
            WriteTimeoutMilliseconds = options.WriteTimeoutMilliseconds
        };
    }

    /// <inheritdoc />
    protected override Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var port = new SerialPort(
            _options.PortName,
            _options.BaudRate,
            _options.Parity,
            _options.DataBits,
            _options.StopBits)
        {
            ReadTimeout = _options.ReadTimeoutMilliseconds,
            WriteTimeout = _options.WriteTimeoutMilliseconds
        };

        try
        {
            port.DataReceived += OnDataReceived;
            port.Open();
            _port = port;
        }
        catch (Exception ex)
        {
            port.DataReceived -= OnDataReceived;
            port.Dispose();
            throw new ZeusChannelException(
                Name,
                $"无法打开串口 {_options.PortName}（通道 {Name}）：{ex.Message}。请确认端口存在、未被其他程序占用；联调阶段可改用 AddVirtualChannel。",
                ex);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task CloseCoreAsync(CancellationToken cancellationToken)
    {
        var port = _port;
        _port = null;
        if (port is null)
        {
            return Task.CompletedTask;
        }

        port.DataReceived -= OnDataReceived;
        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        finally
        {
            port.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var port = _port ?? throw new ZeusChannelException(Name, $"通道 {Name} 的串口句柄已丢失，请重新启动宿主。");
        cancellationToken.ThrowIfCancellationRequested();
        var payload = buffer.ToArray();
        port.Write(payload, 0, payload.Length);
        PublishPacketTrace(ChannelTraceDirection.Sent, payload);
        return Task.CompletedTask;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _port;
        if (port is null || !port.IsOpen)
        {
            return;
        }

        try
        {
            var count = port.BytesToRead;
            if (count <= 0)
            {
                return;
            }

            var buffer = new byte[count];
            var read = port.Read(buffer, 0, count);
            if (read > 0)
            {
                PublishData(buffer.AsSpan(0, read));
            }
        }
        catch (Exception)
        {
            // 接收回调位于框架线程池。此处吞掉瞬时读错误，避免拆掉 SerialPort 内部泵；
            // 持续故障会在下一次写入或关闭时表面化。
        }
    }
}
