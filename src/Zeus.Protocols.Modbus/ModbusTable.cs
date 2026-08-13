namespace Zeus;

/// <summary>
/// Modbus 四类数据区。地址均从 0 起算。
/// </summary>
public enum ModbusTable
{
    /// <summary>保持寄存器，功能码 03 / 06 / 10。</summary>
    HoldingRegister = 0,

    /// <summary>输入寄存器，功能码 04。</summary>
    InputRegister = 1,

    /// <summary>线圈，功能码 01 / 05 / 0F。</summary>
    Coil = 2,

    /// <summary>离散输入，功能码 02。</summary>
    DiscreteInput = 3
}
