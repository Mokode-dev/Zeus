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
/// MC 多块批量读取的一个连续区间。
/// </summary>
/// <param name="DeviceCode">软元件代码。</param>
/// <param name="Address">起始地址。</param>
/// <param name="Points">点数。字块为字数，位块为位数。</param>
public readonly record struct McDeviceRange(McDeviceCode DeviceCode, int Address, ushort Points);

/// <summary>
/// MC 远程控制模式。对应 3E/4E 远程 RUN/STOP 等命令。
/// </summary>
public enum McRemoteControlMode
{
    /// <summary>远程 RUN（0x1001）。</summary>
    Run = 0,

    /// <summary>远程 STOP（0x1002）。</summary>
    Stop = 1,

    /// <summary>远程 PAUSE（0x1003）。</summary>
    Pause = 2,

    /// <summary>远程锁存清除（0x1005）。</summary>
    LatchClear = 3,

    /// <summary>远程复位（0x1006）。</summary>
    Reset = 4
}

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

/// <summary>
/// MC 多块批量读取结果。字值按字块声明顺序拼接，位值按位块声明顺序拼接。
/// </summary>
public sealed class McMultipleBlockReadResult
{
    /// <summary>
    /// 创建多块读取结果。
    /// </summary>
    /// <param name="wordValues">全部字块拼接后的字值。</param>
    /// <param name="bitValues">全部位块拼接后的位值。</param>
    public McMultipleBlockReadResult(IReadOnlyList<ushort> wordValues, IReadOnlyList<bool> bitValues)
    {
        ArgumentNullException.ThrowIfNull(wordValues);
        ArgumentNullException.ThrowIfNull(bitValues);
        WordValues = wordValues.ToArray();
        BitValues = bitValues.ToArray();
    }

    /// <summary>字块结果，顺序与请求的字块一致。</summary>
    public ushort[] WordValues { get; }

    /// <summary>位块结果，顺序与请求的位块一致。</summary>
    public bool[] BitValues { get; }
}
