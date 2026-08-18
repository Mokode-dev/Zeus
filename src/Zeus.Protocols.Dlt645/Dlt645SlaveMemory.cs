namespace Zeus;

/// <summary>
/// DL/T 645 虚拟表计的数据项映像。键为四字节数据项标识，值为未加 0x33 的原始数据区。
/// </summary>
public sealed class Dlt645SlaveMemory
{
    private readonly Dictionary<uint, byte[]> _items = [];

    /// <summary>当前已预置的数据项标识。</summary>
    public IReadOnlyCollection<uint> DataIdentifiers => _items.Keys.ToArray();

    /// <summary>设置原始数据项。</summary>
    public void SetData(uint dataIdentifier, IReadOnlyList<byte> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Dlt645Codec.EnsureDataLength(data.Count);
        _items[dataIdentifier] = data.ToArray();
    }

    /// <summary>尝试读取原始数据项。</summary>
    public bool TryGetData(uint dataIdentifier, out byte[] data)
    {
        if (_items.TryGetValue(dataIdentifier, out var existing))
        {
            data = existing.ToArray();
            return true;
        }

        data = [];
        return false;
    }

    /// <summary>设置 BCD 数值数据项。</summary>
    public void SetBcd(uint dataIdentifier, double value, int byteLength = 4, double scale = 0.01)
        => SetData(dataIdentifier, Dlt645Codec.EncodeBcd(value, byteLength, scale));

    /// <summary>读取 BCD 数值数据项。</summary>
    public double GetBcd(uint dataIdentifier, int byteLength = 4, double scale = 0.01)
    {
        if (!TryGetData(dataIdentifier, out var data))
        {
            throw new ZeusException($"DL/T 645 虚拟表计尚未设置数据项 {Dlt645Codec.FormatDataIdentifier(dataIdentifier)}。");
        }

        if (data.Length < byteLength)
        {
            throw new ZeusException($"DL/T 645 虚拟表计数据项 {Dlt645Codec.FormatDataIdentifier(dataIdentifier)} 长度不足。");
        }

        return Dlt645Codec.DecodeBcd(data.Take(byteLength).ToArray(), scale);
    }
}
