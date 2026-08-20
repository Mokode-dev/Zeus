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

    /// <summary>功能码 0x07 返回的异常状态字节。</summary>
    public byte ExceptionStatus { get; set; }

    /// <summary>功能码 0x11 返回的服务器 ID。</summary>
    public byte ServerId { get; set; } = 0xFF;

    /// <summary>功能码 0x11 返回的运行指示状态。</summary>
    public bool ServerRunIndicatorStatus { get; set; } = true;

    /// <summary>功能码 0x11 返回的厂商自定义附加数据。</summary>
    public byte[] ServerIdAdditionalData { get; set; } = [];

    /// <summary>功能码 0x2B/0x0E 对象 0x00：厂商名。</summary>
    public string VendorName { get; set; } = "Zeus";

    /// <summary>功能码 0x2B/0x0E 对象 0x01：产品代码。</summary>
    public string ProductCode { get; set; } = "VirtualSlave";

    /// <summary>功能码 0x2B/0x0E 对象 0x02：主次版本。</summary>
    public string MajorMinorRevision { get; set; } = "0.17.0";

    /// <summary>文件记录映像，键为文件号和记录号。</summary>
    public Dictionary<(ushort FileNumber, ushort RecordNumber), ushort[]> FileRecords { get; } = [];
}
