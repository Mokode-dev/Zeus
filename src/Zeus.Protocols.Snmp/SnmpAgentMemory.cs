namespace Zeus;

/// <summary>SNMP 虚拟 Agent 的内存 MIB。</summary>
public sealed class SnmpAgentMemory
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SnmpValue> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _writable = new(StringComparer.Ordinal);

    /// <summary>创建内存 MIB，并预置常用 system 组变量。</summary>
    public SnmpAgentMemory()
    {
        SetText("1.3.6.1.2.1.1.1.0", "Zeus SNMP Agent", writable: false);
        Set("1.3.6.1.2.1.1.3.0", SnmpValue.TimeTicks(0), writable: false);
        SetText("1.3.6.1.2.1.1.5.0", "zeus", writable: true);
    }

    /// <summary>设置变量值。</summary>
    public void Set(string oid, SnmpValue value, bool writable = true)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = SnmpCodec.NormalizeOid(oid);
        lock (_gate)
        {
            _values[normalized] = value;
            if (writable)
            {
                _writable.Add(normalized);
            }
            else
            {
                _writable.Remove(normalized);
            }
        }
    }

    /// <summary>设置整数变量。</summary>
    public void SetInteger(string oid, long value, bool writable = true) => Set(oid, SnmpValue.Integer(value), writable);

    /// <summary>设置 Gauge32 变量。</summary>
    public void SetGauge32(string oid, uint value, bool writable = true) => Set(oid, SnmpValue.Gauge32(value), writable);

    /// <summary>设置 Counter32 变量。</summary>
    public void SetCounter32(string oid, uint value, bool writable = false) => Set(oid, SnmpValue.Counter32(value), writable);

    /// <summary>设置文本变量。</summary>
    public void SetText(string oid, string value, bool writable = true) => Set(oid, SnmpValue.Text(value), writable);

    /// <summary>读取变量值。</summary>
    public bool TryGet(string oid, out SnmpValue? value)
    {
        var normalized = SnmpCodec.NormalizeOid(oid);
        lock (_gate)
        {
            return _values.TryGetValue(normalized, out value);
        }
    }

    /// <summary>尝试写入变量。</summary>
    public SnmpErrorStatus TrySet(string oid, SnmpValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = SnmpCodec.NormalizeOid(oid);
        lock (_gate)
        {
            if (!_values.TryGetValue(normalized, out var existing))
            {
                return SnmpErrorStatus.NoSuchName;
            }

            if (!_writable.Contains(normalized))
            {
                return SnmpErrorStatus.NotWritable;
            }

            try
            {
                _values[normalized] = SnmpCodec.Coerce(value, existing.DataType);
                return SnmpErrorStatus.NoError;
            }
            catch (Exception)
            {
                return SnmpErrorStatus.WrongType;
            }
        }
    }
}
