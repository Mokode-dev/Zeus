namespace Zeus;

/// <summary>
/// MC 随机访问的单个软元件地址。
/// </summary>
/// <param name="DeviceCode">软元件代码，例如 D、W、R、ZR、M、X、Y。</param>
/// <param name="Address">软元件地址。</param>
public readonly record struct McDeviceAddress(McDeviceCode DeviceCode, int Address);

/// <summary>
/// MC 随机写入的单字值。
/// </summary>
/// <param name="DeviceCode">软元件代码。</param>
/// <param name="Address">软元件地址。</param>
/// <param name="Value">写入的 16 位值。</param>
public readonly record struct McWordWrite(McDeviceCode DeviceCode, int Address, ushort Value);

/// <summary>
/// MC 随机写入的双字值，占用起始地址开始的两个连续字。
/// </summary>
/// <param name="DeviceCode">软元件代码。</param>
/// <param name="Address">软元件起始地址。</param>
/// <param name="Value">写入的 32 位值。</param>
public readonly record struct McDoubleWordWrite(McDeviceCode DeviceCode, int Address, uint Value);

/// <summary>
/// MC 随机写入的位值。
/// </summary>
/// <param name="DeviceCode">位软元件代码，例如 M、X、Y。</param>
/// <param name="Address">软元件地址。</param>
/// <param name="Value">写入的 ON/OFF 值。</param>
public readonly record struct McBitWrite(McDeviceCode DeviceCode, int Address, bool Value);

/// <summary>
/// MC 随机读取结果，按请求中的 word 与 double word 顺序分别返回。
/// </summary>
public sealed class McRandomReadResult
{
    /// <summary>
    /// 创建随机读取结果。
    /// </summary>
    /// <param name="wordValues">单字读取结果。</param>
    /// <param name="doubleWordValues">双字读取结果。</param>
    public McRandomReadResult(IReadOnlyList<ushort> wordValues, IReadOnlyList<uint> doubleWordValues)
    {
        ArgumentNullException.ThrowIfNull(wordValues);
        ArgumentNullException.ThrowIfNull(doubleWordValues);
        WordValues = wordValues.ToArray();
        DoubleWordValues = doubleWordValues.ToArray();
    }

    /// <summary>单字读取结果，顺序与请求的 word devices 一致。</summary>
    public ushort[] WordValues { get; }

    /// <summary>双字读取结果，顺序与请求的 double word devices 一致。</summary>
    public uint[] DoubleWordValues { get; }
}
