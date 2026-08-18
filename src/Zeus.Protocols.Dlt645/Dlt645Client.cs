namespace Zeus;

/// <summary>
/// 在一条通道上执行 DL/T 645-2007 请求。同一客户端串行发送帧并校验应答。
/// </summary>
public sealed class Dlt645Client : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly Dlt645Options _options;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private TaskCompletionSource<bool>? _dataPulse;

    /// <summary>创建 DL/T 645 客户端并订阅通道。</summary>
    public Dlt645Client(IChannel channel, Dlt645Options? options = null, TimeSpan? timeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _options = CopyOptions(options ?? new Dlt645Options());
        ValidateOptions(_options);
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>当前会话选项副本。</summary>
    public Dlt645Options Options => CopyOptions(_options);

    /// <summary>读取一个 DL/T 645 数据项，返回不含数据项标识的原始数据区。</summary>
    public async Task<byte[]> ReadDataAsync(uint dataIdentifier, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            await _channel.WriteAsync(
                Dlt645Codec.EncodeReadDataRequest(_options.MeterAddress, dataIdentifier, _options.WakeUpPreambleCount),
                cancellationToken).ConfigureAwait(false);

            var response = await WaitForResponseAsync(Dlt645Codec.ReadData, cancellationToken).ConfigureAwait(false);
            EnsureNormalResponse(response, Dlt645Codec.ReadDataResponse, Dlt645Codec.ReadData);
            if (response.Data.Length < 4)
            {
                throw new ZeusProtocolException("DL/T 645 读数据响应缺少 4 字节数据项标识。");
            }

            var actualIdentifier = Dlt645Codec.DecodeDataIdentifier(response.Data);
            if (actualIdentifier != dataIdentifier)
            {
                throw new ZeusProtocolException(
                    $"DL/T 645 响应数据项为 {Dlt645Codec.FormatDataIdentifier(actualIdentifier)}，期望 {Dlt645Codec.FormatDataIdentifier(dataIdentifier)}。");
            }

            return response.Data.Skip(4).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>写入一个 DL/T 645 数据项。密码和操作者代码默认来自 <see cref="Dlt645Options"/>。</summary>
    public async Task WriteDataAsync(
        uint dataIdentifier,
        IReadOnlyList<byte> data,
        string? password = null,
        string? operatorCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            await _channel.WriteAsync(
                Dlt645Codec.EncodeWriteDataRequest(
                    _options.MeterAddress,
                    dataIdentifier,
                    data,
                    password ?? _options.Password,
                    operatorCode ?? _options.OperatorCode,
                    _options.WakeUpPreambleCount),
                cancellationToken).ConfigureAwait(false);

            var response = await WaitForResponseAsync(Dlt645Codec.WriteData, cancellationToken).ConfigureAwait(false);
            EnsureNormalResponse(response, Dlt645Codec.WriteDataResponse, Dlt645Codec.WriteData);
            if (response.Data.Length != 0)
            {
                throw new ZeusProtocolException("DL/T 645 写数据正常响应数据区应为空。请核对表计返回帧。");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>读取低字节在前的 BCD 数据项，并按 <paramref name="scale"/> 换算为工程值。</summary>
    public async Task<double> ReadBcdAsync(uint dataIdentifier, int byteLength, double scale, CancellationToken cancellationToken = default)
    {
        Dlt645Codec.EnsureDataLength(byteLength);
        var data = await ReadDataAsync(dataIdentifier, cancellationToken).ConfigureAwait(false);
        if (data.Length < byteLength)
        {
            throw new ZeusProtocolException(
                $"DL/T 645 数据项 {Dlt645Codec.FormatDataIdentifier(dataIdentifier)} 返回 {data.Length} 字节，少于期望的 {byteLength} 字节。");
        }

        return Dlt645Codec.DecodeBcd(data.Take(byteLength).ToArray(), scale);
    }

    /// <summary>按 BCD 格式写入数据项。</summary>
    public Task WriteBcdAsync(
        uint dataIdentifier,
        double value,
        int byteLength,
        double scale,
        CancellationToken cancellationToken = default)
        => WriteDataAsync(dataIdentifier, Dlt645Codec.EncodeBcd(value, byteLength, scale), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _channel.DataReceived -= OnDataReceived;
        _channel.StateChanged -= OnStateChanged;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Dlt645Frame> WaitForResponseAsync(byte command, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        while (true)
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            lock (_bufferLock)
            {
                if (Dlt645Codec.TryDecodeFrame(_buffer, out var response, out var consumed))
                {
                    _buffer.RemoveRange(0, consumed);
                    return response;
                }

                _dataPulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            try
            {
                await _dataPulse.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ZeusProtocolException(
                    $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 DL/T 645 0x{command:X2} 应答。请检查表地址、串口参数、前导唤醒字节或用 Dlt645SlaveResponder 联调。");
            }
        }
    }

    private void EnsureNormalResponse(Dlt645Frame response, byte expectedControlCode, byte requestCommand)
    {
        if (!string.Equals(response.MeterAddress, _options.MeterAddress, StringComparison.Ordinal))
        {
            throw new ZeusProtocolException(
                $"DL/T 645 响应表地址为 {response.MeterAddress}，期望 {_options.MeterAddress}。请避免多表共享同一客户端。");
        }

        if (response.IsError)
        {
            var errorCode = response.Data.Length > 0 ? response.Data[0] : (byte)0;
            throw new Dlt645Exception(requestCommand, errorCode);
        }

        if (response.ControlCode != expectedControlCode)
        {
            throw new ZeusProtocolException($"DL/T 645 响应控制码为 0x{response.ControlCode:X2}，期望 0x{expectedControlCode:X2}。");
        }
    }

    private void OnDataReceived(object? sender, ChannelDataReceivedEventArgs e)
    {
        lock (_bufferLock)
        {
            foreach (var value in e.Data.Span)
            {
                _buffer.Add(value);
            }

            _dataPulse?.TrySetResult(true);
            _dataPulse = null;
        }
    }

    private void OnStateChanged(object? sender, ChannelStateChangedEventArgs e)
    {
        if (e.Current is ChannelState.Faulted or ChannelState.Closed)
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
                _dataPulse?.TrySetException(new ZeusProtocolException(
                    $"通道 {_channel.Name} 已变为 {e.Current}，未完成的 DL/T 645 请求已取消。"));
                _dataPulse = null;
            }
        }
    }

    private static Dlt645Options CopyOptions(Dlt645Options source)
        => new()
        {
            MeterAddress = source.MeterAddress,
            WakeUpPreambleCount = source.WakeUpPreambleCount,
            Password = source.Password,
            OperatorCode = source.OperatorCode
        };

    private static void ValidateOptions(Dlt645Options options)
    {
        Dlt645Codec.ValidateAddress(options.MeterAddress);
        if (options.WakeUpPreambleCount is < 0 or > 16)
        {
            throw new ZeusProtocolException($"DL/T 645 前导 0xFE 数量必须介于 0 与 16 之间，当前为 {options.WakeUpPreambleCount}。");
        }

        _ = Dlt645Codec.EncodeWriteDataRequest(options.MeterAddress, 0, [], options.Password, options.OperatorCode, 0);
    }
}
