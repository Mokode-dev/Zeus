namespace Zeus;

/// <summary>
/// Mitsubishi MC 虚拟 PLC 的软元件映像。
/// </summary>
public sealed class McSlaveMemory
{
    /// <summary>
    /// 创建指定容量的映像。
    /// </summary>
    /// <param name="dataRegisters">D 数据寄存器数量。</param>
    /// <param name="internalRelays">M 内部继电器数量。</param>
    public McSlaveMemory(int dataRegisters = 4096, int internalRelays = 8192)
        : this(dataRegisters, internalRelays, 8192, 8192, 4096, 4096, 4096)
    {
    }

    /// <summary>
    /// 创建指定容量的映像。
    /// </summary>
    /// <param name="dataRegisters">D 数据寄存器数量。</param>
    /// <param name="internalRelays">M 内部继电器数量。</param>
    /// <param name="inputRelays">X 输入继电器数量。</param>
    /// <param name="outputRelays">Y 输出继电器数量。</param>
    /// <param name="linkRegisters">W 链接寄存器数量。</param>
    /// <param name="fileRegisters">R 文件寄存器数量。</param>
    /// <param name="extendedFileRegisters">ZR 扩展文件寄存器数量。</param>
    public McSlaveMemory(
        int dataRegisters,
        int internalRelays,
        int inputRelays,
        int outputRelays,
        int linkRegisters,
        int fileRegisters,
        int extendedFileRegisters)
    {
        DataRegisters = new ushort[dataRegisters];
        InternalRelays = new bool[internalRelays];
        InputRelays = new bool[inputRelays];
        OutputRelays = new bool[outputRelays];
        LinkRegisters = new ushort[linkRegisters];
        FileRegisters = new ushort[fileRegisters];
        ExtendedFileRegisters = new ushort[extendedFileRegisters];
    }

    /// <summary>D 数据寄存器。</summary>
    public ushort[] DataRegisters { get; }

    /// <summary>M 内部继电器。</summary>
    public bool[] InternalRelays { get; }

    /// <summary>X 输入继电器。</summary>
    public bool[] InputRelays { get; }

    /// <summary>Y 输出继电器。</summary>
    public bool[] OutputRelays { get; }

    /// <summary>W 链接寄存器。</summary>
    public ushort[] LinkRegisters { get; }

    /// <summary>R 文件寄存器。</summary>
    public ushort[] FileRegisters { get; }

    /// <summary>ZR 扩展文件寄存器。</summary>
    public ushort[] ExtendedFileRegisters { get; }
}
