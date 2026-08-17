namespace Zeus;

/// <summary>
/// FINS 内存区编码访问粒度。
/// </summary>
public enum FinsMemoryAreaKind
{
    /// <summary>位访问区，每个项目返回 0 或 1。</summary>
    Bit = 0,

    /// <summary>字访问区，每个项目返回 16 位字。</summary>
    Word = 1
}
