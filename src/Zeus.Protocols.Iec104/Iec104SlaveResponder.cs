namespace Zeus;

/// <summary>
/// IEC 60870-5-104 内存虚拟站。实现 <see cref="IVirtualResponder"/>，可直接交给 <c>AddVirtualChannel</c>。
/// </summary>
public sealed class Iec104SlaveResponder : IVirtualResponder
{
    private readonly Iec104Options _options;
    private readonly Iec104StationMemory _memory;
    private ushort _sendSequence;
    private ushort _receiveSequence;
    private bool _started;

    /// <summary>创建虚拟 IEC104 站。</summary>
    public Iec104SlaveResponder(Iec104Options? options = null, Iec104StationMemory? memory = null)
    {
        _options = CopyOptions(options ?? new Iec104Options());
        Iec104Codec.ValidateOptions(_options);
        _memory = memory ?? CreateDefaultMemory();
    }

    /// <summary>可在测试中预置或断言的信息对象映像。</summary>
    public Iec104StationMemory Memory => _memory;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Respond(ReadOnlyMemory<byte> request)
    {
        if (!Iec104Codec.TryDecodeApdu(request.ToArray(), out var apdu, out _))
        {
            return null;
        }

        if (Iec104Codec.IsStartDataTransferActivation(apdu))
        {
            _started = true;
            return Iec104Codec.EncodeStartDataTransferConfirmation();
        }

        if (Iec104Codec.IsTestFrameActivation(apdu))
        {
            return Iec104Codec.EncodeTestFrameConfirmation();
        }

        if (!_started || apdu.Format != Iec104FrameFormat.I)
        {
            return null;
        }

        _receiveSequence = NextSequence(apdu.SendSequence);
        if (Iec104Codec.TryDecodeInterrogationCommand(apdu.Asdu, _options, out _))
        {
            return HandleInterrogation();
        }

        if (Iec104Codec.TryDecodeCommand(apdu.Asdu, out var address, out var dataType, out var value))
        {
            _memory.Set(new Iec104InformationObject(address, dataType, value, Cause: Iec104CauseOfTransmission.Spontaneous));
            return Iec104Codec.EncodeCommandActivationConfirmation(apdu.Asdu, NextSendSequence(), _receiveSequence);
        }

        return null;
    }

    private byte[] HandleInterrogation()
    {
        var frames = new List<byte>();
        frames.AddRange(Iec104Codec.EncodeInterrogationConfirmation(_options, Iec104CauseOfTransmission.ActivationConfirmation, NextSendSequence(), _receiveSequence));
        foreach (var value in _memory.Snapshot)
        {
            frames.AddRange(Iec104Codec.EncodeInformationObject(_options, value, Iec104CauseOfTransmission.InterrogatedByStation, NextSendSequence(), _receiveSequence));
        }

        frames.AddRange(Iec104Codec.EncodeInterrogationConfirmation(_options, Iec104CauseOfTransmission.ActivationTermination, NextSendSequence(), _receiveSequence));
        return frames.ToArray();
    }

    private ushort NextSendSequence()
    {
        var current = _sendSequence;
        _sendSequence = NextSequence(_sendSequence);
        return current;
    }

    private static ushort NextSequence(ushort value) => (ushort)((value + 1) & 0x7FFF);

    private static Iec104Options CopyOptions(Iec104Options source)
        => new()
        {
            CommonAddress = source.CommonAddress,
            OriginatorAddress = source.OriginatorAddress,
            InterrogationQualifier = source.InterrogationQualifier
        };

    private static Iec104StationMemory CreateDefaultMemory()
    {
        var memory = new Iec104StationMemory();
        memory.SetSinglePoint(1, true);
        memory.SetScaled(100, 253);
        memory.SetShortFloat(200, 25.3);
        memory.SetNormalized(300, 0.5);
        return memory;
    }
}
