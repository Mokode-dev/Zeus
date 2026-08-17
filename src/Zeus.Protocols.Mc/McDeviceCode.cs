namespace Zeus;

/// <summary>
/// MC 常用软元件代码。
/// </summary>
public enum McDeviceCode : byte
{
    /// <summary>内部继电器 M。</summary>
    InternalRelay = 0x90,

    /// <summary>输入继电器 X。</summary>
    InputRelay = 0x9C,

    /// <summary>输出继电器 Y。</summary>
    OutputRelay = 0x9D,

    /// <summary>数据寄存器 D。</summary>
    DataRegister = 0xA8,

    /// <summary>链接寄存器 W。</summary>
    LinkRegister = 0xB4,

    /// <summary>文件寄存器 R。</summary>
    FileRegister = 0xAF,

    /// <summary>扩展文件寄存器 ZR。</summary>
    ExtendedFileRegister = 0xB0
}
