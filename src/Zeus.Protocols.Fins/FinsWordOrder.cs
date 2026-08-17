namespace Zeus;

/// <summary>
/// 32 位工程值在连续 FINS 字中的排列顺序。
/// </summary>
public enum FinsWordOrder
{
    /// <summary>高字在前，例如 D100 是高 16 位，D101 是低 16 位。</summary>
    HighWordFirst = 0,

    /// <summary>低字在前，例如 D100 是低 16 位，D101 是高 16 位。</summary>
    LowWordFirst = 1
}
