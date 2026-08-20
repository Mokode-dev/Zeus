namespace Zeus;

/// <summary>
/// 本版本支持的 Modbus 功能码。
/// </summary>
public static class ModbusFunction
{
    /// <summary>读线圈。</summary>
    public const byte ReadCoils = 0x01;

    /// <summary>读离散输入。</summary>
    public const byte ReadDiscreteInputs = 0x02;

    /// <summary>读保持寄存器。</summary>
    public const byte ReadHoldingRegisters = 0x03;

    /// <summary>读输入寄存器。</summary>
    public const byte ReadInputRegisters = 0x04;

    /// <summary>写单个线圈。</summary>
    public const byte WriteSingleCoil = 0x05;

    /// <summary>写单个保持寄存器。</summary>
    public const byte WriteSingleRegister = 0x06;

    /// <summary>读异常状态。</summary>
    public const byte ReadExceptionStatus = 0x07;

    /// <summary>诊断。</summary>
    public const byte Diagnostics = 0x08;

    /// <summary>写多个线圈。</summary>
    public const byte WriteMultipleCoils = 0x0F;

    /// <summary>写多个保持寄存器。</summary>
    public const byte WriteMultipleRegisters = 0x10;

    /// <summary>报告服务器 ID。</summary>
    public const byte ReportServerId = 0x11;

    /// <summary>掩码写保持寄存器。</summary>
    public const byte MaskWriteRegister = 0x16;

    /// <summary>读写多个保持寄存器。</summary>
    public const byte ReadWriteMultipleRegisters = 0x17;

    /// <summary>读文件记录。</summary>
    public const byte ReadFileRecord = 0x14;

    /// <summary>写文件记录。</summary>
    public const byte WriteFileRecord = 0x15;

    /// <summary>封装接口（MEI），设备识别使用子类型 0x0E。</summary>
    public const byte EncapsulatedInterfaceTransport = 0x2B;

    /// <summary>读设备识别的 MEI 类型。</summary>
    public const byte ReadDeviceIdentificationMei = 0x0E;
}
