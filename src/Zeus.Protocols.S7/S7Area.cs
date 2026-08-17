namespace Zeus;

/// <summary>
/// Siemens S7 可访问的存储区。
/// </summary>
public enum S7Area
{
    /// <summary>输入区 I。</summary>
    Inputs = 0,

    /// <summary>输出区 Q。</summary>
    Outputs = 1,

    /// <summary>标志位/中间寄存器区 M。</summary>
    Merkers = 2,

    /// <summary>数据块 DB。</summary>
    DataBlock = 3
}
