namespace Zeus;

/// <summary>SNMP varbind 中的一项变量。</summary>
public sealed record SnmpVariable(string Oid, SnmpValue Value);
