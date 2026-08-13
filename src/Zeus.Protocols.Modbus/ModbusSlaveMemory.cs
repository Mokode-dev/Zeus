namespace Zeus;

/// <summary>
/// 内存中的线圈与寄存器映像。地址从 0 开始，对应协议里的起始地址。
/// </summary>
public sealed class ModbusSlaveMemory
{
    /// <summary>
    /// 创建指定容量的映像。
    /// </summary>
    /// <param name="holdingRegisters">保持寄存器数量。</param>
    /// <param name="inputRegisters">输入寄存器数量。</param>
    /// <param name="coils">线圈数量。</param>
    /// <param name="discreteInputs">离散输入数量。</param>
    public ModbusSlaveMemory(
        int holdingRegisters = 256,
        int inputRegisters = 256,
        int coils = 256,
        int discreteInputs = 256)
    {
        HoldingRegisters = new ushort[holdingRegisters];
        InputRegisters = new ushort[inputRegisters];
        Coils = new bool[coils];
        DiscreteInputs = new bool[discreteInputs];
    }

    /// <summary>保持寄存器。</summary>
    public ushort[] HoldingRegisters { get; }

    /// <summary>输入寄存器。</summary>
    public ushort[] InputRegisters { get; }

    /// <summary>线圈。</summary>
    public bool[] Coils { get; }

    /// <summary>离散输入。</summary>
    public bool[] DiscreteInputs { get; }
}
