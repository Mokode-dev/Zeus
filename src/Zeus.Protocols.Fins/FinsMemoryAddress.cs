namespace Zeus;

/// <summary>
/// FINS 内存区地址。字区通常 bitOffset 为 0；位区使用 wordAddress + bitOffset。
/// </summary>
public readonly record struct FinsMemoryAddress(FinsMemoryAreaCode Area, ushort WordAddress, byte BitOffset = 0)
{
    /// <inheritdoc />
    public override string ToString() => $"{Area.Name}:{WordAddress}.{BitOffset}";
}
