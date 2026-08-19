using System.Globalization;
using System.Net;
using System.Text;

namespace Zeus;

/// <summary>SNMP v2c BER 编解码。</summary>
internal static class SnmpCodec
{
    public const int Version2c = 1;
    public const byte GetRequest = 0xA0;
    public const byte GetResponse = 0xA2;
    public const byte SetRequest = 0xA3;

    private const byte Sequence = 0x30;
    private const byte Integer = 0x02;
    private const byte OctetString = 0x04;
    private const byte Null = 0x05;
    private const byte ObjectIdentifier = 0x06;
    private const byte IpAddress = 0x40;
    private const byte Counter32 = 0x41;
    private const byte Gauge32 = 0x42;
    private const byte TimeTicks = 0x43;
    private const byte NoSuchObject = 0x80;
    private const byte NoSuchInstance = 0x81;
    private const byte EndOfMibView = 0x82;

    public static byte[] EncodeGetRequest(string community, int requestId, string oid)
        => EncodeMessage(community, GetRequest, requestId, SnmpErrorStatus.NoError, 0, [new SnmpVariable(NormalizeOid(oid), new SnmpValue(SnmpDataType.OctetString, null))]);

    public static byte[] EncodeSetRequest(string community, int requestId, string oid, SnmpValue value)
        => EncodeMessage(community, SetRequest, requestId, SnmpErrorStatus.NoError, 0, [new SnmpVariable(NormalizeOid(oid), value)]);

    public static byte[] EncodeResponse(string community, int requestId, SnmpErrorStatus status, int errorIndex, IReadOnlyList<SnmpVariable> variables)
        => EncodeMessage(community, GetResponse, requestId, status, errorIndex, variables);

    public static SnmpMessage DecodeMessage(ReadOnlySpan<byte> data)
    {
        var reader = new BerReader(data);
        var message = reader.ReadElement(Sequence);
        var inner = new BerReader(message);
        var version = (int)DecodeInteger(inner.ReadElement(Integer));
        if (version != Version2c)
        {
            throw new ZeusProtocolException($"SNMP 版本 {version} 不受支持，当前仅支持 v2c。");
        }

        var community = Encoding.ASCII.GetString(inner.ReadElement(OctetString));
        var pduTag = inner.PeekTag();
        if (pduTag is not (GetRequest or GetResponse or SetRequest))
        {
            throw new ZeusProtocolException($"SNMP PDU 类型 0x{pduTag:X2} 不受支持。");
        }

        var pdu = new BerReader(inner.ReadElement(pduTag));
        var requestId = (int)DecodeInteger(pdu.ReadElement(Integer));
        var errorStatus = (SnmpErrorStatus)DecodeInteger(pdu.ReadElement(Integer));
        var errorIndex = (int)DecodeInteger(pdu.ReadElement(Integer));
        var variables = DecodeVarBindList(pdu.ReadElement(Sequence));

        inner.EnsureConsumed("SNMP message");
        pdu.EnsureConsumed("SNMP PDU");
        reader.EnsureConsumed("SNMP packet");
        return new SnmpMessage(community, pduTag, requestId, errorStatus, errorIndex, variables);
    }

    public static string NormalizeOid(string oid)
    {
        if (string.IsNullOrWhiteSpace(oid))
        {
            throw new ZeusProtocolException("SNMP OID 不能为空。");
        }

        var normalized = oid.Trim();
        while (normalized.StartsWith(".", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            throw new ZeusProtocolException($"SNMP OID「{oid}」无效，至少需要两个节点。");
        }

        var numbers = new uint[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!uint.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                throw new ZeusProtocolException($"SNMP OID「{oid}」包含非法节点 {parts[i]}。");
            }
        }

        if (numbers[0] > 2 || (numbers[0] < 2 && numbers[1] > 39))
        {
            throw new ZeusProtocolException($"SNMP OID「{oid}」前两个节点无效。");
        }

        return string.Join('.', numbers);
    }

    public static SnmpValue Coerce(SnmpValue value, SnmpDataType dataType)
    {
        if (value.DataType == dataType)
        {
            return value;
        }

        if (dataType == SnmpDataType.Text && value.DataType == SnmpDataType.OctetString && value.Value is byte[] bytes)
        {
            return SnmpValue.Text(Encoding.UTF8.GetString(bytes));
        }

        if (dataType == SnmpDataType.OctetString && value.DataType == SnmpDataType.Text && value.Value is string text)
        {
            return SnmpValue.OctetString(Encoding.UTF8.GetBytes(text));
        }

        return SnmpValue.FromObject(dataType, ToEngineeringValue(value, null));
    }

    public static object ToEngineeringValue(SnmpValue value, double? scale)
    {
        object raw = value.DataType switch
        {
            SnmpDataType.Integer => Convert.ToInt64(value.Value, CultureInfo.InvariantCulture),
            SnmpDataType.Counter32 or SnmpDataType.Gauge32 or SnmpDataType.TimeTicks => Convert.ToUInt32(value.Value, CultureInfo.InvariantCulture),
            SnmpDataType.Text => Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            SnmpDataType.OctetString when value.Value is byte[] bytes => bytes.ToArray(),
            SnmpDataType.OctetString => Array.Empty<byte>(),
            SnmpDataType.ObjectIdentifier => NormalizeOid(Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty),
            SnmpDataType.IpAddress when value.Value is IPAddress address => address.ToString(),
            SnmpDataType.IpAddress => Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => throw new ZeusProtocolException($"不支持的 SNMP 值类型 {value.DataType}。")
        };

        if (scale is { } factor && raw is IConvertible)
        {
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture) * factor;
        }

        return raw;
    }

    public static SnmpValue FromEngineeringValue(SnmpDataType dataType, object value, double? scale)
    {
        if (scale is { } factor && IsNumeric(dataType))
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture) / factor;
            return SnmpValue.FromObject(dataType, Math.Round(number, MidpointRounding.AwayFromZero));
        }

        return SnmpValue.FromObject(dataType, value);
    }

    public static bool IsNumeric(SnmpDataType dataType)
        => dataType is SnmpDataType.Integer or SnmpDataType.Counter32 or SnmpDataType.Gauge32 or SnmpDataType.TimeTicks;

    private static byte[] EncodeMessage(
        string community,
        byte pduTag,
        int requestId,
        SnmpErrorStatus status,
        int errorIndex,
        IReadOnlyList<SnmpVariable> variables)
    {
        var pdu = EncodeElement(
            pduTag,
            Concat(
                EncodeInteger(requestId),
                EncodeInteger((int)status),
                EncodeInteger(errorIndex),
                EncodeVarBindList(variables)));

        return EncodeElement(
            Sequence,
            Concat(
                EncodeInteger(Version2c),
                EncodeElement(OctetString, Encoding.ASCII.GetBytes(community ?? string.Empty)),
                pdu));
    }

    private static byte[] EncodeVarBindList(IReadOnlyList<SnmpVariable> variables)
        => EncodeElement(Sequence, Concat(variables.Select(EncodeVarBind).ToArray()));

    private static byte[] EncodeVarBind(SnmpVariable variable)
        => EncodeElement(Sequence, Concat(EncodeOid(variable.Oid), EncodeValue(variable.Value)));

    private static IReadOnlyList<SnmpVariable> DecodeVarBindList(ReadOnlySpan<byte> data)
    {
        var reader = new BerReader(data);
        var variables = new List<SnmpVariable>();
        while (!reader.Consumed)
        {
            var item = new BerReader(reader.ReadElement(Sequence));
            var oid = DecodeOid(item.ReadElement(ObjectIdentifier));
            var tag = item.PeekTag();
            var value = DecodeValue(tag, item.ReadElement(tag));
            item.EnsureConsumed("SNMP varbind");
            variables.Add(new SnmpVariable(oid, value));
        }

        return variables;
    }

    private static byte[] EncodeValue(SnmpValue value)
    {
        if (value.Value is null)
        {
            return EncodeElement(Null, []);
        }

        return value.DataType switch
        {
            SnmpDataType.Integer => EncodeElement(Integer, EncodeSignedInteger(Convert.ToInt64(value.Value, CultureInfo.InvariantCulture))),
            SnmpDataType.Text => EncodeElement(OctetString, Encoding.UTF8.GetBytes(Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty)),
            SnmpDataType.OctetString when value.Value is byte[] bytes => EncodeElement(OctetString, bytes),
            SnmpDataType.OctetString => EncodeElement(OctetString, Encoding.UTF8.GetBytes(Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty)),
            SnmpDataType.ObjectIdentifier => EncodeOid(Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty),
            SnmpDataType.IpAddress => EncodeElement(IpAddress, EncodeIpAddress(value.Value)),
            SnmpDataType.Counter32 => EncodeElement(Counter32, EncodeUnsignedInteger(Convert.ToUInt32(value.Value, CultureInfo.InvariantCulture))),
            SnmpDataType.Gauge32 => EncodeElement(Gauge32, EncodeUnsignedInteger(Convert.ToUInt32(value.Value, CultureInfo.InvariantCulture))),
            SnmpDataType.TimeTicks => EncodeElement(TimeTicks, EncodeUnsignedInteger(Convert.ToUInt32(value.Value, CultureInfo.InvariantCulture))),
            _ => throw new ZeusProtocolException($"不支持的 SNMP 值类型 {value.DataType}。")
        };
    }

    private static SnmpValue DecodeValue(byte tag, ReadOnlySpan<byte> value)
        => tag switch
        {
            Null => new SnmpValue(SnmpDataType.OctetString, null),
            Integer => SnmpValue.Integer(DecodeInteger(value)),
            OctetString => SnmpValue.OctetString(value.ToArray()),
            ObjectIdentifier => SnmpValue.ObjectIdentifier(DecodeOid(value)),
            IpAddress => SnmpValue.IpAddress(new IPAddress(value.ToArray()).ToString()),
            Counter32 => SnmpValue.Counter32(DecodeUInt32(value)),
            Gauge32 => SnmpValue.Gauge32(DecodeUInt32(value)),
            TimeTicks => SnmpValue.TimeTicks(DecodeUInt32(value)),
            NoSuchObject => throw new ZeusProtocolException("SNMP 返回 noSuchObject。"),
            NoSuchInstance => throw new ZeusProtocolException("SNMP 返回 noSuchInstance。"),
            EndOfMibView => throw new ZeusProtocolException("SNMP 返回 endOfMibView。"),
            _ => throw new ZeusProtocolException($"SNMP 值标签 0x{tag:X2} 不受支持。")
        };

    private static byte[] EncodeIpAddress(object value)
    {
        var address = value is IPAddress ip ? ip : IPAddress.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            throw new ZeusProtocolException($"SNMP IpAddress 仅支持 IPv4，当前为 {address}。");
        }

        return bytes;
    }

    private static byte[] EncodeOid(string oid)
    {
        var normalized = NormalizeOid(oid);
        var numbers = normalized.Split('.').Select(item => uint.Parse(item, CultureInfo.InvariantCulture)).ToArray();
        var content = new List<byte>();
        EncodeOidSubIdentifier((numbers[0] * 40) + numbers[1], content);
        for (var i = 2; i < numbers.Length; i++)
        {
            EncodeOidSubIdentifier(numbers[i], content);
        }

        return EncodeElement(ObjectIdentifier, content.ToArray());
    }

    private static string DecodeOid(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            throw new ZeusProtocolException("SNMP OID 内容为空。");
        }

        var identifiers = new List<uint>();
        var index = 0;
        var first = DecodeOidSubIdentifier(value, ref index);
        if (first < 40)
        {
            identifiers.Add(0);
            identifiers.Add(first);
        }
        else if (first < 80)
        {
            identifiers.Add(1);
            identifiers.Add(first - 40);
        }
        else
        {
            identifiers.Add(2);
            identifiers.Add(first - 80);
        }

        while (index < value.Length)
        {
            identifiers.Add(DecodeOidSubIdentifier(value, ref index));
        }

        return string.Join('.', identifiers);
    }

    private static void EncodeOidSubIdentifier(uint value, List<byte> output)
    {
        Span<byte> stack = stackalloc byte[5];
        var count = 0;
        do
        {
            stack[count++] = (byte)(value & 0x7F);
            value >>= 7;
        }
        while (value != 0);

        for (var i = count - 1; i >= 0; i--)
        {
            var current = stack[i];
            if (i != 0)
            {
                current |= 0x80;
            }

            output.Add(current);
        }
    }

    private static uint DecodeOidSubIdentifier(ReadOnlySpan<byte> data, ref int index)
    {
        uint value = 0;
        for (var i = 0; i < 5; i++)
        {
            if (index >= data.Length)
            {
                throw new ZeusProtocolException("SNMP OID 子节点编码未结束。");
            }

            var current = data[index++];
            value = (value << 7) | (uint)(current & 0x7F);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new ZeusProtocolException("SNMP OID 子节点超过 32 位范围。");
    }

    private static byte[] EncodeInteger(int value)
        => EncodeElement(Integer, EncodeSignedInteger(value));

    private static long DecodeInteger(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || data.Length > 8)
        {
            throw new ZeusProtocolException($"SNMP INTEGER 长度 {data.Length} 无效。");
        }

        long value = (data[0] & 0x80) != 0 ? -1 : 0;
        foreach (var current in data)
        {
            value = (value << 8) | current;
        }

        return value;
    }

    private static uint DecodeUInt32(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || data.Length > 5)
        {
            throw new ZeusProtocolException($"SNMP 无符号整数长度 {data.Length} 无效。");
        }

        ulong value = 0;
        foreach (var current in data)
        {
            value = (value << 8) | current;
        }

        if (value > uint.MaxValue)
        {
            throw new ZeusProtocolException($"SNMP 无符号整数 {value} 超出 UInt32 范围。");
        }

        return (uint)value;
    }

    private static byte[] EncodeSignedInteger(long value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        var offset = 0;
        while (offset < bytes.Length - 1)
        {
            var current = bytes[offset];
            var next = bytes[offset + 1];
            if ((current == 0x00 && (next & 0x80) == 0) || (current == 0xFF && (next & 0x80) != 0))
            {
                offset++;
                continue;
            }

            break;
        }

        return bytes[offset..];
    }

    private static byte[] EncodeUnsignedInteger(uint value)
    {
        Span<byte> bytes = stackalloc byte[5];
        bytes[1] = (byte)(value >> 24);
        bytes[2] = (byte)(value >> 16);
        bytes[3] = (byte)(value >> 8);
        bytes[4] = (byte)value;
        var offset = 1;
        while (offset < 4 && bytes[offset] == 0)
        {
            offset++;
        }

        if ((bytes[offset] & 0x80) != 0)
        {
            offset--;
            bytes[offset] = 0;
        }

        return bytes[offset..5].ToArray();
    }

    private static byte[] EncodeElement(byte tag, ReadOnlySpan<byte> content)
    {
        var length = EncodeLength(content.Length);
        var result = new byte[1 + length.Length + content.Length];
        result[0] = tag;
        length.CopyTo(result.AsSpan(1));
        content.CopyTo(result.AsSpan(1 + length.Length));
        return result;
    }

    private static byte[] EncodeLength(int length)
    {
        if (length < 0)
        {
            throw new ZeusProtocolException("SNMP BER 长度不能为负数。");
        }

        if (length < 0x80)
        {
            return [(byte)length];
        }

        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)(length >> 24);
        bytes[1] = (byte)(length >> 16);
        bytes[2] = (byte)(length >> 8);
        bytes[3] = (byte)length;
        var offset = 0;
        while (offset < 3 && bytes[offset] == 0)
        {
            offset++;
        }

        var count = 4 - offset;
        var result = new byte[1 + count];
        result[0] = (byte)(0x80 | count);
        bytes[offset..4].CopyTo(result.AsSpan(1));
        return result;
    }

    private static byte[] Concat(params byte[][] segments)
    {
        var length = segments.Sum(item => item.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var segment in segments)
        {
            segment.CopyTo(result.AsSpan(offset));
            offset += segment.Length;
        }

        return result;
    }

    private sealed class BerReader
    {
        private readonly ReadOnlyMemory<byte> _data;
        private int _offset;

        public BerReader(ReadOnlySpan<byte> data) => _data = data.ToArray();

        public bool Consumed => _offset >= _data.Length;

        public byte PeekTag()
        {
            if (Consumed)
            {
                throw new ZeusProtocolException("SNMP BER 数据提前结束。");
            }

            return _data.Span[_offset];
        }

        public ReadOnlySpan<byte> ReadElement(byte expectedTag)
        {
            var tag = PeekTag();
            if (tag != expectedTag)
            {
                throw new ZeusProtocolException($"SNMP BER 标签 0x{tag:X2} 异常，期望 0x{expectedTag:X2}。");
            }

            _offset++;
            var length = ReadLength();
            if (_offset + length > _data.Length)
            {
                throw new ZeusProtocolException("SNMP BER 长度超出报文范围。");
            }

            var span = _data.Span.Slice(_offset, length);
            _offset += length;
            return span;
        }

        public void EnsureConsumed(string context)
        {
            if (!Consumed)
            {
                throw new ZeusProtocolException($"{context} 末尾存在多余 BER 数据。");
            }
        }

        private int ReadLength()
        {
            if (Consumed)
            {
                throw new ZeusProtocolException("SNMP BER 长度缺失。");
            }

            var first = _data.Span[_offset++];
            if ((first & 0x80) == 0)
            {
                return first;
            }

            var count = first & 0x7F;
            if (count is 0 or > 4 || _offset + count > _data.Length)
            {
                throw new ZeusProtocolException("SNMP BER 长度字段无效。");
            }

            var length = 0;
            for (var i = 0; i < count; i++)
            {
                length = (length << 8) | _data.Span[_offset++];
            }

            return length;
        }
    }
}

internal sealed record SnmpMessage(
    string Community,
    byte PduType,
    int RequestId,
    SnmpErrorStatus ErrorStatus,
    int ErrorIndex,
    IReadOnlyList<SnmpVariable> Variables);
