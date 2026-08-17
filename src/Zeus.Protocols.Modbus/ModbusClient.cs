namespace Zeus;

/// <summary>
/// 在一条通道上执行 Modbus 请求。同一客户端串行发送，避免 RTU 半双工冲突。
/// </summary>
public sealed class ModbusClient : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly ModbusTransport _transport;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = [];
    private ushort _transactionId;
    private TaskCompletionSource<bool>? _dataPulse;

    /// <summary>
    /// 创建客户端并订阅通道。
    /// </summary>
    /// <param name="channel">传输通道。</param>
    /// <param name="transport">RTU 或 TCP。</param>
    /// <param name="timeout">应答超时，默认 1 秒。</param>
    public ModbusClient(IChannel channel, ModbusTransport transport, TimeSpan? timeout = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _transport = transport;
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _channel.DataReceived += OnDataReceived;
        _channel.StateChanged += OnStateChanged;
    }

    /// <summary>绑定的通道。</summary>
    public IChannel Channel => _channel;

    /// <summary>线上封装。</summary>
    public ModbusTransport Transport => _transport;

    /// <summary>
    /// 发送 PDU 并等待匹配的响应 PDU。
    /// </summary>
    /// <param name="unitId">从站/单元标识。</param>
    /// <param name="requestPdu">功能码 + 数据。</param>
    /// <param name="cancellationToken">取消等待。</param>
    public async Task<byte[]> ExecuteAsync(byte unitId, ReadOnlyMemory<byte> requestPdu, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
            }

            var transactionId = NextTransactionId();
            var adu = ModbusCodec.EncodeRequest(_transport, unitId, requestPdu.Span, transactionId);
            await _channel.WriteAsync(adu, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);

            while (true)
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                lock (_bufferLock)
                {
                    if (ModbusCodec.TryDecodeResponse(
                            _transport,
                            _buffer,
                            requestPdu.Span,
                            transactionId,
                            out var responseUnit,
                            out var pdu,
                            out var consumed))
                    {
                        _buffer.RemoveRange(0, consumed);
                        if (responseUnit != unitId)
                        {
                            throw new ZeusProtocolException(
                                $"Modbus 响应地址为 {responseUnit}，期望 {unitId}。请确认总线没有其它主机抢答。");
                        }

                        return UnwrapPdu(unitId, requestPdu.Span[0], pdu);
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
                        $"通道 {_channel.Name} 在 {_timeout.TotalMilliseconds:0} ms 内未收到完整 Modbus 应答。请检查从站地址、波特率，或用 ModbusSlaveResponder 挂到虚拟通道上联调。");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>读保持寄存器。</summary>
    public Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort address, ushort quantity, CancellationToken cancellationToken = default)
        => ReadRegistersAsync(unitId, ModbusFunction.ReadHoldingRegisters, address, quantity, cancellationToken);

    /// <summary>读输入寄存器。</summary>
    public Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort address, ushort quantity, CancellationToken cancellationToken = default)
        => ReadRegistersAsync(unitId, ModbusFunction.ReadInputRegisters, address, quantity, cancellationToken);

    /// <summary>读线圈。</summary>
    public Task<bool[]> ReadCoilsAsync(byte unitId, ushort address, ushort quantity, CancellationToken cancellationToken = default)
        => ReadBitsAsync(unitId, ModbusFunction.ReadCoils, address, quantity, cancellationToken);

    /// <summary>读离散输入。</summary>
    public Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort address, ushort quantity, CancellationToken cancellationToken = default)
        => ReadBitsAsync(unitId, ModbusFunction.ReadDiscreteInputs, address, quantity, cancellationToken);

    /// <summary>读异常状态（功能码 0x07）。</summary>
    public async Task<byte> ReadExceptionStatusAsync(byte unitId, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(unitId, new byte[] { ModbusFunction.ReadExceptionStatus }, cancellationToken).ConfigureAwait(false);
        if (response.Length < 2 || response[0] != ModbusFunction.ReadExceptionStatus)
        {
            throw new ZeusProtocolException("读异常状态的响应长度异常。请核对从站功能码 0x07 实现。");
        }

        return response[1];
    }

    /// <summary>执行诊断回显（功能码 0x08，子功能 0x0000）。</summary>
    public async Task<ushort> DiagnosticsReturnQueryDataAsync(
        byte unitId,
        ushort data,
        CancellationToken cancellationToken = default)
    {
        var pdu = new byte[5];
        pdu[0] = ModbusFunction.Diagnostics;
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(1, 2), 0x0000);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(3, 2), data);
        var response = await ExecuteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
        EnsureEcho(pdu, response, "诊断回显");
        return ModbusCodec.ReadUInt16BigEndian(response.AsSpan(3, 2));
    }

    /// <summary>报告服务器 ID（功能码 0x11）。</summary>
    public async Task<ModbusServerId> ReportServerIdAsync(byte unitId, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(unitId, new byte[] { ModbusFunction.ReportServerId }, cancellationToken).ConfigureAwait(false);
        if (response.Length < 4 || response[0] != ModbusFunction.ReportServerId)
        {
            throw new ZeusProtocolException("报告服务器 ID 的响应长度异常。请核对从站功能码 0x11 实现。");
        }

        var byteCount = response[1];
        if (byteCount < 2 || response.Length < 2 + byteCount)
        {
            throw new ZeusProtocolException("报告服务器 ID 的字节数异常。请核对从站功能码 0x11 实现。");
        }

        var additionalLength = byteCount - 2;
        var additionalData = additionalLength == 0
            ? Array.Empty<byte>()
            : response.AsSpan(4, additionalLength).ToArray();
        return new ModbusServerId(response[2], response[3] != 0, additionalData);
    }

    /// <summary>写单个保持寄存器。</summary>
    public async Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort value, CancellationToken cancellationToken = default)
    {
        var pdu = new byte[5];
        pdu[0] = ModbusFunction.WriteSingleRegister;
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(1, 2), address);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(3, 2), value);
        var response = await ExecuteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
        EnsureEcho(pdu, response, "写单个寄存器");
    }

    /// <summary>写多个保持寄存器。</summary>
    public async Task WriteMultipleRegistersAsync(byte unitId, ushort address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0 || values.Count > 123)
        {
            throw new ZeusProtocolException("一次写入的保持寄存器数量必须在 1 到 123 之间。");
        }

        var pdu = new byte[6 + (values.Count * 2)];
        pdu[0] = ModbusFunction.WriteMultipleRegisters;
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(1, 2), address);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(3, 2), (ushort)values.Count);
        pdu[5] = (byte)(values.Count * 2);
        for (var i = 0; i < values.Count; i++)
        {
            ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(6 + (i * 2), 2), values[i]);
        }

        var response = await ExecuteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
        if (response.Length < 5
            || response[0] != ModbusFunction.WriteMultipleRegisters
            || ModbusCodec.ReadUInt16BigEndian(response.AsSpan(1, 2)) != address
            || ModbusCodec.ReadUInt16BigEndian(response.AsSpan(3, 2)) != values.Count)
        {
            throw new ZeusProtocolException("写多个寄存器的响应与请求不一致，请核对从站实现。");
        }
    }

    /// <summary>按 AND / OR 掩码修改单个保持寄存器。</summary>
    public async Task MaskWriteRegisterAsync(
        byte unitId,
        ushort address,
        ushort andMask,
        ushort orMask,
        CancellationToken cancellationToken = default)
    {
        var pdu = new byte[7];
        pdu[0] = ModbusFunction.MaskWriteRegister;
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(1, 2), address);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(3, 2), andMask);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(5, 2), orMask);
        var response = await ExecuteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
        EnsureEcho(pdu, response, "掩码写寄存器");
    }

    /// <summary>读写多个保持寄存器（功能码 0x17）。写操作先执行，再返回读取区间。</summary>
    public async Task<ushort[]> ReadWriteMultipleRegistersAsync(
        byte unitId,
        ushort readAddress,
        ushort readQuantity,
        ushort writeAddress,
        IReadOnlyList<ushort> writeValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writeValues);
        EnsureQuantity(readQuantity, 125, "读取寄存器");
        if (writeValues.Count == 0 || writeValues.Count > 121)
        {
            throw new ZeusProtocolException("一次读写事务写入的保持寄存器数量必须在 1 到 121 之间。");
        }

        var pdu = new byte[10 + (writeValues.Count * 2)];
        pdu[0] = ModbusFunction.ReadWriteMultipleRegisters;
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(1, 2), readAddress);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(3, 2), readQuantity);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(5, 2), writeAddress);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(7, 2), (ushort)writeValues.Count);
        pdu[9] = (byte)(writeValues.Count * 2);
        for (var i = 0; i < writeValues.Count; i++)
        {
            ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(10 + (i * 2), 2), writeValues[i]);
        }

        var response = await ExecuteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
        if (response.Length < 2 + (readQuantity * 2)
            || response[0] != ModbusFunction.ReadWriteMultipleRegisters
            || response[1] != readQuantity * 2)
        {
            throw new ZeusProtocolException("读写多个寄存器的响应长度异常。请核对从站功能码 0x17 实现。");
        }

        var values = new ushort[readQuantity];
        for (var i = 0; i < readQuantity; i++)
        {
            values[i] = ModbusCodec.ReadUInt16BigEndian(response.AsSpan(2 + (i * 2), 2));
        }

        return values;
    }

    /// <summary>写单个线圈。</summary>
    public async Task WriteSingleCoilAsync(byte unitId, ushort address, bool value, CancellationToken cancellationToken = default)
    {
        var pdu = new byte[5];
        pdu[0] = ModbusFunction.WriteSingleCoil;
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(1, 2), address);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(3, 2), (ushort)(value ? 0xFF00 : 0x0000));
        var response = await ExecuteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
        EnsureEcho(pdu, response, "写单个线圈");
    }

    /// <summary>写多个线圈。</summary>
    public async Task WriteMultipleCoilsAsync(byte unitId, ushort address, IReadOnlyList<bool> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0 || values.Count > 1968)
        {
            throw new ZeusProtocolException("一次写入的线圈数量必须在 1 到 1968 之间。");
        }

        var byteCount = ModbusCodec.CoilByteCount(values.Count);
        var pdu = new byte[6 + byteCount];
        pdu[0] = ModbusFunction.WriteMultipleCoils;
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(1, 2), address);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(3, 2), (ushort)values.Count);
        pdu[5] = (byte)byteCount;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i])
            {
                pdu[6 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        var response = await ExecuteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
        if (response.Length < 5
            || response[0] != ModbusFunction.WriteMultipleCoils
            || ModbusCodec.ReadUInt16BigEndian(response.AsSpan(1, 2)) != address
            || ModbusCodec.ReadUInt16BigEndian(response.AsSpan(3, 2)) != values.Count)
        {
            throw new ZeusProtocolException("写多个线圈的响应与请求不一致，请核对从站实现。");
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _channel.DataReceived -= OnDataReceived;
        _channel.StateChanged -= OnStateChanged;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<ushort[]> ReadRegistersAsync(
        byte unitId,
        byte function,
        ushort address,
        ushort quantity,
        CancellationToken cancellationToken)
    {
        EnsureQuantity(quantity, 125, "寄存器");
        var pdu = BuildAddressQuantityPdu(function, address, quantity);
        var response = await ExecuteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
        if (response.Length < 2 || response[0] != function || response[1] != quantity * 2)
        {
            throw new ZeusProtocolException($"读寄存器响应长度异常（功能 0x{function:X2}）。");
        }

        var values = new ushort[quantity];
        for (var i = 0; i < quantity; i++)
        {
            values[i] = ModbusCodec.ReadUInt16BigEndian(response.AsSpan(2 + (i * 2), 2));
        }

        return values;
    }

    private async Task<bool[]> ReadBitsAsync(
        byte unitId,
        byte function,
        ushort address,
        ushort quantity,
        CancellationToken cancellationToken)
    {
        EnsureQuantity(quantity, 2000, "线圈/离散输入");
        var pdu = BuildAddressQuantityPdu(function, address, quantity);
        var response = await ExecuteAsync(unitId, pdu, cancellationToken).ConfigureAwait(false);
        var byteCount = ModbusCodec.CoilByteCount(quantity);
        if (response.Length < 2 + byteCount || response[0] != function || response[1] != byteCount)
        {
            throw new ZeusProtocolException($"读位响应长度异常（功能 0x{function:X2}）。");
        }

        var values = new bool[quantity];
        for (var i = 0; i < quantity; i++)
        {
            values[i] = (response[2 + (i / 8)] & (1 << (i % 8))) != 0;
        }

        return values;
    }

    private static byte[] BuildAddressQuantityPdu(byte function, ushort address, ushort quantity)
    {
        var pdu = new byte[5];
        pdu[0] = function;
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(1, 2), address);
        ModbusCodec.WriteUInt16BigEndian(pdu.AsSpan(3, 2), quantity);
        return pdu;
    }

    private static void EnsureQuantity(int quantity, int max, string name)
    {
        if (quantity <= 0 || quantity > max)
        {
            throw new ZeusProtocolException($"{name}数量必须在 1 到 {max} 之间，当前为 {quantity}。");
        }
    }

    private static void EnsureEcho(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response, string operation)
    {
        if (response.Length < request.Length || !response[..request.Length].SequenceEqual(request))
        {
            throw new ZeusProtocolException($"{operation}的响应与请求不一致。");
        }
    }

    private static byte[] UnwrapPdu(byte unitId, byte requestFunction, byte[] pdu)
    {
        if (pdu.Length >= 2 && (pdu[0] & 0x80) != 0)
        {
            throw new ModbusException(unitId, (byte)(pdu[0] & 0x7F), (ModbusExceptionCode)pdu[1]);
        }

        if (pdu.Length == 0 || pdu[0] != requestFunction)
        {
            throw new ZeusProtocolException(
                $"Modbus 响应功能码为 0x{(pdu.Length == 0 ? 0 : pdu[0]):X2}，期望 0x{requestFunction:X2}。");
        }

        return pdu;
    }

    private ushort NextTransactionId()
    {
        _transactionId++;
        if (_transactionId == 0)
        {
            _transactionId = 1;
        }

        return _transactionId;
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
                    $"通道 {_channel.Name} 已变为 {e.Current}，未完成的 Modbus 请求已取消。"));
                _dataPulse = null;
            }
        }
    }
}
