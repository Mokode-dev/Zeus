namespace Zeus;

/// <summary>
/// IEC104 虚拟站内存映像。地址为 3 字节 IOA。
/// </summary>
public sealed class Iec104StationMemory
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Iec104InformationObject> _values = [];

    /// <summary>当前全部信息对象快照。</summary>
    public IReadOnlyList<Iec104InformationObject> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _values.Values.OrderBy(item => item.Address).ToArray();
            }
        }
    }

    /// <summary>设置单点信息。</summary>
    public void SetSinglePoint(int address, bool value, byte quality = 0)
        => Set(new Iec104InformationObject(NormalizeAddress(address), Iec104DataType.SinglePoint, value, quality));

    /// <summary>设置归一化测量值，范围应为 -1 到 1。</summary>
    public void SetNormalized(int address, double value, byte quality = 0)
    {
        if (!double.IsFinite(value) || value is < -1 or > 1)
        {
            throw new ZeusProtocolException($"IEC104 归一化值必须介于 -1 与 1 之间，当前为 {value}。");
        }

        Set(new Iec104InformationObject(NormalizeAddress(address), Iec104DataType.Normalized, value, quality));
    }

    /// <summary>设置标度化测量值。</summary>
    public void SetScaled(int address, short value, byte quality = 0)
        => Set(new Iec104InformationObject(NormalizeAddress(address), Iec104DataType.Scaled, value, quality));

    /// <summary>设置短浮点测量值。</summary>
    public void SetShortFloat(int address, double value, byte quality = 0)
    {
        if (!double.IsFinite(value))
        {
            throw new ZeusProtocolException("IEC104 短浮点值必须是有限数值。");
        }

        Set(new Iec104InformationObject(NormalizeAddress(address), Iec104DataType.ShortFloat, value, quality));
    }

    internal bool TryGet(int address, out Iec104InformationObject value)
    {
        lock (_gate)
        {
            return _values.TryGetValue(address, out value);
        }
    }

    internal void Set(Iec104InformationObject value)
    {
        var address = NormalizeAddress(value.Address);
        lock (_gate)
        {
            _values[address] = value with { Address = address };
        }
    }

    private static int NormalizeAddress(int address)
    {
        Iec104Codec.ValidateInformationObjectAddress(address, nameof(address));
        return address;
    }
}
