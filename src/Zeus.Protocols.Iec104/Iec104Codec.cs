using System.Buffers.Binary;
using System.Globalization;

namespace Zeus;

internal static class Iec104Codec
{
    public const byte Start = 0x68;
    public const byte TypeSinglePoint = 1;
    public const byte TypeNormalized = 9;
    public const byte TypeScaled = 11;
    public const byte TypeShortFloat = 13;
    public const byte TypeSingleCommand = 45;
    public const byte TypeSetpointNormalized = 48;
    public const byte TypeSetpointScaled = 49;
    public const byte TypeSetpointShortFloat = 50;
    public const byte TypeInterrogationCommand = 100;

    public static void ValidateOptions(Iec104Options options)
    {
        if (options.CommonAddress is < 0 or > ushort.MaxValue)
        {
            throw new ZeusProtocolException($"IEC104 commonAddress 必须介于 0 与 65535 之间，当前为 {options.CommonAddress}。");
        }

        if (options.OriginatorAddress is < 0 or > byte.MaxValue)
        {
            throw new ZeusProtocolException($"IEC104 originatorAddress 必须介于 0 与 255 之间，当前为 {options.OriginatorAddress}。");
        }

        if (options.InterrogationQualifier is < 0 or > byte.MaxValue)
        {
            throw new ZeusProtocolException($"IEC104 interrogationQualifier 必须介于 0 与 255 之间，当前为 {options.InterrogationQualifier}。");
        }
    }

    public static void ValidateInformationObjectAddress(int address, string name)
    {
        if (address is < 0 or > 0xFFFFFF)
        {
            throw new ZeusProtocolException($"IEC104 {name} 必须介于 0 与 16777215 之间，当前为 {address}。");
        }
    }

    public static byte[] EncodeStartDataTransferActivation() => [Start, 4, 0x07, 0x00, 0x00, 0x00];

    public static byte[] EncodeStartDataTransferConfirmation() => [Start, 4, 0x0B, 0x00, 0x00, 0x00];

    public static byte[] EncodeTestFrameConfirmation() => [Start, 4, 0x83, 0x00, 0x00, 0x00];

    public static byte[] EncodeInterrogationCommand(Iec104Options options, ushort sendSequence, ushort receiveSequence)
    {
        var asdu = CreateAsduHeader(TypeInterrogationCommand, Iec104CauseOfTransmission.Activation, options);
        asdu.AddRange([0x00, 0x00, 0x00, (byte)options.InterrogationQualifier]);
        return EncodeIFrame(sendSequence, receiveSequence, asdu.ToList());
    }

    public static byte[] EncodeInterrogationConfirmation(
        Iec104Options options,
        Iec104CauseOfTransmission cause,
        ushort sendSequence,
        ushort receiveSequence)
    {
        var asdu = CreateAsduHeader(TypeInterrogationCommand, cause, options);
        asdu.AddRange([0x00, 0x00, 0x00, (byte)options.InterrogationQualifier]);
        return EncodeIFrame(sendSequence, receiveSequence, asdu.ToList());
    }

    public static byte[] EncodeInformationObject(
        Iec104Options options,
        Iec104InformationObject value,
        Iec104CauseOfTransmission cause,
        ushort sendSequence,
        ushort receiveSequence)
    {
        ValidateInformationObjectAddress(value.Address, nameof(value.Address));
        var typeId = ToMeasuredType(value.DataType);
        var asdu = CreateAsduHeader(typeId, cause, options);
        WriteInformationObjectAddress(asdu, value.Address);
        WriteMeasuredPayload(asdu, value);
        return EncodeIFrame(sendSequence, receiveSequence, asdu);
    }

    public static byte[] EncodeSingleCommand(
        Iec104Options options,
        int address,
        bool command,
        ushort sendSequence,
        ushort receiveSequence,
        bool select = false)
    {
        var asdu = CreateAsduHeader(TypeSingleCommand, Iec104CauseOfTransmission.Activation, options);
        WriteInformationObjectAddress(asdu, address);
        asdu.Add((byte)((select ? 0x80 : 0x00) | (command ? 0x01 : 0x00)));
        return EncodeIFrame(sendSequence, receiveSequence, asdu);
    }

    public static byte[] EncodeSetpoint(
        Iec104Options options,
        int address,
        Iec104DataType dataType,
        object value,
        ushort sendSequence,
        ushort receiveSequence,
        bool select = false)
    {
        var typeId = ToCommandType(dataType);
        var asdu = CreateAsduHeader(typeId, Iec104CauseOfTransmission.Activation, options);
        WriteInformationObjectAddress(asdu, address);
        switch (dataType)
        {
            case Iec104DataType.Normalized:
                WriteInt16LittleEndian(asdu, EncodeNormalized(Convert.ToDouble(value, CultureInfo.InvariantCulture)));
                asdu.Add((byte)(select ? 0x80 : 0x00));
                break;
            case Iec104DataType.Scaled:
                WriteInt16LittleEndian(asdu, Convert.ToInt16(value, CultureInfo.InvariantCulture));
                asdu.Add((byte)(select ? 0x80 : 0x00));
                break;
            case Iec104DataType.ShortFloat:
                WriteSingleLittleEndian(asdu, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                asdu.Add((byte)(select ? 0x80 : 0x00));
                break;
            default:
                throw new ZeusProtocolException($"IEC104 {dataType} 不支持设点编码。");
        }

        return EncodeIFrame(sendSequence, receiveSequence, asdu);
    }

    public static byte[] EncodeCommandActivationConfirmation(byte[] requestAsdu, ushort sendSequence, ushort receiveSequence)
    {
        if (requestAsdu.Length < 6)
        {
            throw new ZeusProtocolException("IEC104 命令确认缺少 ASDU 头。");
        }

        var asdu = requestAsdu.ToArray();
        asdu[2] = (byte)Iec104CauseOfTransmission.ActivationConfirmation;
        return EncodeIFrame(sendSequence, receiveSequence, asdu.ToList());
    }

    public static bool TryDecodeApdu(IReadOnlyList<byte> buffer, out Iec104Apdu apdu, out int consumed)
    {
        apdu = default;
        consumed = 0;
        var start = IndexOf(buffer, Start, 0);
        if (start < 0)
        {
            return false;
        }

        if (buffer.Count - start < 6)
        {
            return false;
        }

        var length = buffer[start + 1];
        if (length < 4)
        {
            throw new ZeusProtocolException($"IEC104 APDU 长度 {length} 小于 4。请确认对端是 IEC104。");
        }

        var frameLength = 2 + length;
        if (buffer.Count - start < frameLength)
        {
            return false;
        }

        var c0 = buffer[start + 2];
        var c1 = buffer[start + 3];
        var c2 = buffer[start + 4];
        var c3 = buffer[start + 5];
        var asdu = new byte[length - 4];
        for (var i = 0; i < asdu.Length; i++)
        {
            asdu[i] = buffer[start + 6 + i];
        }

        if ((c0 & 0x01) == 0)
        {
            apdu = new Iec104Apdu(Iec104FrameFormat.I, DecodeSequence(c0, c1), DecodeSequence(c2, c3), c0, asdu);
        }
        else if ((c0 & 0x03) == 0x01)
        {
            apdu = new Iec104Apdu(Iec104FrameFormat.S, 0, DecodeSequence(c2, c3), c0, asdu);
        }
        else
        {
            apdu = new Iec104Apdu(Iec104FrameFormat.U, 0, 0, c0, asdu);
        }

        consumed = start + frameLength;
        return true;
    }

    public static Iec104AsduHeader DecodeAsduHeader(byte[] asdu)
    {
        if (asdu.Length < 6)
        {
            throw new ZeusProtocolException("IEC104 ASDU 长度不足 6 字节。");
        }

        var cause = (Iec104CauseOfTransmission)(asdu[2] & 0x3F);
        var commonAddress = BinaryPrimitives.ReadUInt16LittleEndian(asdu.AsSpan(4, 2));
        return new Iec104AsduHeader(asdu[0], (asdu[1] & 0x80) != 0, (byte)(asdu[1] & 0x7F), cause, asdu[3], commonAddress);
    }

    public static IReadOnlyList<Iec104InformationObject> DecodeInformationObjects(byte[] asdu)
    {
        var header = DecodeAsduHeader(asdu);
        var dataType = header.TypeId switch
        {
            TypeSinglePoint => Iec104DataType.SinglePoint,
            TypeNormalized => Iec104DataType.Normalized,
            TypeScaled => Iec104DataType.Scaled,
            TypeShortFloat => Iec104DataType.ShortFloat,
            _ => (Iec104DataType)0
        };

        if (dataType == 0 || header.Count == 0)
        {
            return [];
        }

        var payloadLength = dataType switch
        {
            Iec104DataType.SinglePoint => 1,
            Iec104DataType.Normalized => 3,
            Iec104DataType.Scaled => 3,
            Iec104DataType.ShortFloat => 5,
            _ => throw new ZeusProtocolException($"IEC104 不支持解码 {dataType}。")
        };

        var result = new List<Iec104InformationObject>(header.Count);
        var offset = 6;
        var baseAddress = 0;
        if (header.Sequence)
        {
            EnsureRemaining(asdu, offset, 3, "连续信息对象地址");
            baseAddress = ReadInformationObjectAddress(asdu, offset);
            offset += 3;
        }

        for (var i = 0; i < header.Count; i++)
        {
            int address;
            if (header.Sequence)
            {
                address = baseAddress + i;
            }
            else
            {
                EnsureRemaining(asdu, offset, 3, "信息对象地址");
                address = ReadInformationObjectAddress(asdu, offset);
                offset += 3;
            }

            EnsureRemaining(asdu, offset, payloadLength, "信息对象载荷");
            result.Add(DecodeMeasuredValue(address, dataType, asdu, offset, header.Cause));
            offset += payloadLength;
        }

        return result;
    }

    public static bool TryDecodeInterrogationCommand(byte[] asdu, Iec104Options options, out byte qualifier)
    {
        qualifier = 0;
        if (asdu.Length < 10 || asdu[0] != TypeInterrogationCommand)
        {
            return false;
        }

        var header = DecodeAsduHeader(asdu);
        if (header.Cause != Iec104CauseOfTransmission.Activation || header.CommonAddress != options.CommonAddress)
        {
            return false;
        }

        qualifier = asdu[9];
        return true;
    }

    public static bool TryDecodeCommand(byte[] asdu, out int address, out Iec104DataType dataType, out object value)
    {
        address = 0;
        dataType = default;
        value = false;
        if (asdu.Length < 10)
        {
            return false;
        }

        var header = DecodeAsduHeader(asdu);
        if (header.Cause != Iec104CauseOfTransmission.Activation)
        {
            return false;
        }

        address = ReadInformationObjectAddress(asdu, 6);
        switch (header.TypeId)
        {
            case TypeSingleCommand:
                dataType = Iec104DataType.SinglePoint;
                value = (asdu[9] & 0x01) != 0;
                return true;
            case TypeSetpointNormalized:
                EnsureRemaining(asdu, 9, 3, "归一化设点");
                dataType = Iec104DataType.Normalized;
                value = DecodeNormalized(BinaryPrimitives.ReadInt16LittleEndian(asdu.AsSpan(9, 2)));
                return true;
            case TypeSetpointScaled:
                EnsureRemaining(asdu, 9, 3, "标度化设点");
                dataType = Iec104DataType.Scaled;
                value = BinaryPrimitives.ReadInt16LittleEndian(asdu.AsSpan(9, 2));
                return true;
            case TypeSetpointShortFloat:
                EnsureRemaining(asdu, 9, 5, "短浮点设点");
                dataType = Iec104DataType.ShortFloat;
                value = ReadSingleLittleEndian(asdu, 9);
                return true;
            default:
                return false;
        }
    }

    public static byte ToMeasuredType(Iec104DataType dataType)
        => dataType switch
        {
            Iec104DataType.SinglePoint => TypeSinglePoint,
            Iec104DataType.Normalized => TypeNormalized,
            Iec104DataType.Scaled => TypeScaled,
            Iec104DataType.ShortFloat => TypeShortFloat,
            _ => throw new ZeusProtocolException($"IEC104 不支持测量类型 {dataType}。")
        };

    public static byte ToCommandType(Iec104DataType dataType)
        => dataType switch
        {
            Iec104DataType.SinglePoint => TypeSingleCommand,
            Iec104DataType.Normalized => TypeSetpointNormalized,
            Iec104DataType.Scaled => TypeSetpointScaled,
            Iec104DataType.ShortFloat => TypeSetpointShortFloat,
            _ => throw new ZeusProtocolException($"IEC104 不支持命令类型 {dataType}。")
        };

    public static bool IsStartDataTransferActivation(Iec104Apdu apdu) => apdu.Format == Iec104FrameFormat.U && apdu.Control == 0x07;

    public static bool IsStartDataTransferConfirmation(Iec104Apdu apdu) => apdu.Format == Iec104FrameFormat.U && apdu.Control == 0x0B;

    public static bool IsTestFrameActivation(Iec104Apdu apdu) => apdu.Format == Iec104FrameFormat.U && apdu.Control == 0x43;

    private static byte[] EncodeIFrame(ushort sendSequence, ushort receiveSequence, List<byte> asdu)
    {
        if (asdu.Count > 249)
        {
            throw new ZeusProtocolException("IEC104 单个 ASDU 不能超过 249 字节。");
        }

        var frame = new byte[6 + asdu.Count];
        frame[0] = Start;
        frame[1] = (byte)(4 + asdu.Count);
        WriteSequence(frame.AsSpan(2, 2), sendSequence);
        WriteSequence(frame.AsSpan(4, 2), receiveSequence);
        asdu.CopyTo(frame, 6);
        return frame;
    }

    private static List<byte> CreateAsduHeader(byte typeId, Iec104CauseOfTransmission cause, Iec104Options options)
    {
        ValidateOptions(options);
        return
        [
            typeId,
            0x01,
            (byte)cause,
            (byte)options.OriginatorAddress,
            (byte)(options.CommonAddress & 0xFF),
            (byte)((options.CommonAddress >> 8) & 0xFF)
        ];
    }

    private static void WriteMeasuredPayload(List<byte> asdu, Iec104InformationObject value)
    {
        switch (value.DataType)
        {
            case Iec104DataType.SinglePoint:
                asdu.Add((byte)((value.Quality & 0xF0) | (Convert.ToBoolean(value.Value, CultureInfo.InvariantCulture) ? 0x01 : 0x00)));
                break;
            case Iec104DataType.Normalized:
                WriteInt16LittleEndian(asdu, EncodeNormalized(Convert.ToDouble(value.Value, CultureInfo.InvariantCulture)));
                asdu.Add(value.Quality);
                break;
            case Iec104DataType.Scaled:
                WriteInt16LittleEndian(asdu, Convert.ToInt16(value.Value, CultureInfo.InvariantCulture));
                asdu.Add(value.Quality);
                break;
            case Iec104DataType.ShortFloat:
                WriteSingleLittleEndian(asdu, Convert.ToSingle(value.Value, CultureInfo.InvariantCulture));
                asdu.Add(value.Quality);
                break;
            default:
                throw new ZeusProtocolException($"IEC104 不支持编码 {value.DataType}。");
        }
    }

    private static Iec104InformationObject DecodeMeasuredValue(
        int address,
        Iec104DataType dataType,
        byte[] asdu,
        int offset,
        Iec104CauseOfTransmission cause)
    {
        return dataType switch
        {
            Iec104DataType.SinglePoint => new Iec104InformationObject(address, dataType, (asdu[offset] & 0x01) != 0, (byte)(asdu[offset] & 0xF0), cause),
            Iec104DataType.Normalized => new Iec104InformationObject(address, dataType, DecodeNormalized(BinaryPrimitives.ReadInt16LittleEndian(asdu.AsSpan(offset, 2))), asdu[offset + 2], cause),
            Iec104DataType.Scaled => new Iec104InformationObject(address, dataType, BinaryPrimitives.ReadInt16LittleEndian(asdu.AsSpan(offset, 2)), asdu[offset + 2], cause),
            Iec104DataType.ShortFloat => new Iec104InformationObject(address, dataType, ReadSingleLittleEndian(asdu, offset), asdu[offset + 4], cause),
            _ => throw new ZeusProtocolException($"IEC104 不支持解码 {dataType}。")
        };
    }

    private static short EncodeNormalized(double value)
    {
        if (!double.IsFinite(value) || value is < -1 or > 1)
        {
            throw new ZeusProtocolException($"IEC104 归一化值必须介于 -1 与 1 之间，当前为 {value}。");
        }

        return (short)Math.Round(value * 32767.0, MidpointRounding.AwayFromZero);
    }

    private static double DecodeNormalized(short raw) => raw / 32767.0;

    private static void WriteInformationObjectAddress(List<byte> asdu, int address)
    {
        ValidateInformationObjectAddress(address, nameof(address));
        asdu.Add((byte)(address & 0xFF));
        asdu.Add((byte)((address >> 8) & 0xFF));
        asdu.Add((byte)((address >> 16) & 0xFF));
    }

    private static int ReadInformationObjectAddress(byte[] buffer, int offset)
        => buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);

    private static void WriteInt16LittleEndian(List<byte> buffer, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        buffer.Add(bytes[0]);
        buffer.Add(bytes[1]);
    }

    private static void WriteSingleLittleEndian(List<byte> buffer, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        buffer.AddRange(bytes.ToArray());
    }

    private static float ReadSingleLittleEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(offset, 4));

    private static ushort DecodeSequence(byte low, byte high)
        => (ushort)(((low | (high << 8)) >> 1) & 0x7FFF);

    private static void WriteSequence(Span<byte> destination, ushort sequence)
        => BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)((sequence & 0x7FFF) << 1));

    private static int IndexOf(IReadOnlyList<byte> buffer, byte value, int start)
    {
        for (var i = start; i < buffer.Count; i++)
        {
            if (buffer[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private static void EnsureRemaining(byte[] buffer, int offset, int count, string part)
    {
        if (buffer.Length - offset < count)
        {
            throw new ZeusProtocolException($"IEC104 {part} 长度不足，期望 {count} 字节。");
        }
    }
}

internal enum Iec104FrameFormat
{
    I,
    S,
    U
}

internal readonly record struct Iec104Apdu(
    Iec104FrameFormat Format,
    ushort SendSequence,
    ushort ReceiveSequence,
    byte Control,
    byte[] Asdu);

internal readonly record struct Iec104AsduHeader(
    byte TypeId,
    bool Sequence,
    byte Count,
    Iec104CauseOfTransmission Cause,
    byte OriginatorAddress,
    ushort CommonAddress);
