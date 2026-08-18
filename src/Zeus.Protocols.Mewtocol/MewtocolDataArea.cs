namespace Zeus;

/// <summary>
/// Panasonic MEWTOCOL-COM 数据寄存器区。地址为协议内 0 基字地址。
/// </summary>
public enum MewtocolDataArea
{
    /// <summary>DT 数据寄存器。</summary>
    DataRegister = 0,

    /// <summary>LD 链接数据寄存器。</summary>
    LinkDataRegister = 1,

    /// <summary>FL 文件寄存器。</summary>
    FileRegister = 2
}
