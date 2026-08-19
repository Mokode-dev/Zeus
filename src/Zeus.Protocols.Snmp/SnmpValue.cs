using System.Globalization;
using System.Net;
using System.Text;

namespace Zeus;

/// <summary>一个 SNMP 变量值。</summary>
public sealed record SnmpValue(SnmpDataType DataType, object? Value)
{
    /// <summary>创建有符号整数值。</summary>
    public static SnmpValue Integer(long value) => new(SnmpDataType.Integer, value);

    /// <summary>创建字节串值。</summary>
    public static SnmpValue OctetString(byte[] value) => new(SnmpDataType.OctetString, value.ToArray());

    /// <summary>创建 UTF-8 文本值。</summary>
    public static SnmpValue Text(string value) => new(SnmpDataType.Text, value ?? string.Empty);

    /// <summary>创建 OID 值。</summary>
    public static SnmpValue ObjectIdentifier(string value) => new(SnmpDataType.ObjectIdentifier, SnmpCodec.NormalizeOid(value));

    /// <summary>创建 IPv4 地址值。</summary>
    public static SnmpValue IpAddress(string value) => new(SnmpDataType.IpAddress, IPAddress.Parse(value));

    /// <summary>创建 Counter32 值。</summary>
    public static SnmpValue Counter32(uint value) => new(SnmpDataType.Counter32, value);

    /// <summary>创建 Gauge32 值。</summary>
    public static SnmpValue Gauge32(uint value) => new(SnmpDataType.Gauge32, value);

    /// <summary>创建 TimeTicks 值。</summary>
    public static SnmpValue TimeTicks(uint value) => new(SnmpDataType.TimeTicks, value);

    /// <summary>按目标类型和值创建 SNMP 值。</summary>
    public static SnmpValue FromObject(SnmpDataType dataType, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return dataType switch
        {
            SnmpDataType.Integer => Integer(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            SnmpDataType.Counter32 => Counter32(Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            SnmpDataType.Gauge32 => Gauge32(Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            SnmpDataType.TimeTicks => TimeTicks(Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            SnmpDataType.Text => Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
            SnmpDataType.OctetString when value is byte[] bytes => OctetString(bytes),
            SnmpDataType.OctetString => OctetString(Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)),
            SnmpDataType.ObjectIdentifier => ObjectIdentifier(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
            SnmpDataType.IpAddress => IpAddress(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
            _ => throw new ZeusException($"不支持的 SNMP 数据类型 {dataType}。")
        };
    }
}
