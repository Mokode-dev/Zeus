namespace Zeus;

/// <summary>SNMP 变量值类型。</summary>
public enum SnmpDataType
{
    /// <summary>有符号整数。</summary>
    Integer = 0,

    /// <summary>字节串。</summary>
    OctetString = 1,

    /// <summary>UTF-8 文本，线上仍按 OCTET STRING 传输。</summary>
    Text = 2,

    /// <summary>对象标识符。</summary>
    ObjectIdentifier = 3,

    /// <summary>IPv4 地址。</summary>
    IpAddress = 4,

    /// <summary>32 位计数器。</summary>
    Counter32 = 5,

    /// <summary>32 位无符号量表值。</summary>
    Gauge32 = 6,

    /// <summary>TimeTicks，单位 1/100 秒。</summary>
    TimeTicks = 7
}
