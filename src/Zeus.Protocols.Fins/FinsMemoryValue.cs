namespace Zeus;

/// <summary>
/// FINS 多点读取返回的单个值。
/// </summary>
public sealed class FinsMemoryValue
{
    /// <summary>创建多点读取结果。</summary>
    public FinsMemoryValue(FinsMemoryAddress address, bool? bitValue, ushort? wordValue)
    {
        Address = address;
        BitValue = bitValue;
        WordValue = wordValue;
    }

    /// <summary>请求地址。</summary>
    public FinsMemoryAddress Address { get; }

    /// <summary>位值。仅位区有效。</summary>
    public bool? BitValue { get; }

    /// <summary>字值。仅字区有效。</summary>
    public ushort? WordValue { get; }
}
