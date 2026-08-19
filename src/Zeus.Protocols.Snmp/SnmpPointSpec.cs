namespace Zeus;

/// <summary>一个 SNMP OID 点的声明。</summary>
public sealed record SnmpPointSpec(
    string Name,
    string Oid,
    SnmpDataType DataType,
    PointValueKind Kind,
    double? Scale,
    PointAlarmLimits? AlarmLimits,
    bool Writable);
