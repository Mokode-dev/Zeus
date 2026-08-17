namespace Zeus;

/// <summary>
/// Siemens S7 虚拟 PLC 的内存映像。
/// </summary>
public sealed class S7SlaveMemory
{
    private readonly Dictionary<int, byte[]> _dataBlocks = new();

    /// <summary>
    /// 创建指定容量的映像。
    /// </summary>
    /// <param name="inputs">输入区 I 字节数。</param>
    /// <param name="outputs">输出区 Q 字节数。</param>
    /// <param name="markers">标志区 M 字节数。</param>
    /// <param name="defaultDataBlockSize">自动创建 DB 时的默认字节数。</param>
    public S7SlaveMemory(int inputs = 1024, int outputs = 1024, int markers = 4096, int defaultDataBlockSize = 4096)
    {
        if (inputs < 0 || outputs < 0 || markers < 0 || defaultDataBlockSize <= 0)
        {
            throw new ZeusException("S7 虚拟 PLC 内存容量不能为负数，默认 DB 容量必须大于 0。");
        }

        Inputs = new byte[inputs];
        Outputs = new byte[outputs];
        Markers = new byte[markers];
        DefaultDataBlockSize = defaultDataBlockSize;
    }

    /// <summary>输入区 I。</summary>
    public byte[] Inputs { get; }

    /// <summary>输出区 Q。</summary>
    public byte[] Outputs { get; }

    /// <summary>标志区 M。</summary>
    public byte[] Markers { get; }

    /// <summary>自动创建 DB 时的默认容量。</summary>
    public int DefaultDataBlockSize { get; }

    /// <summary>
    /// 获取或创建一个 DB 块。若已存在但容量不足，会扩展并保留原有内容。
    /// </summary>
    /// <param name="dbNumber">DB 块号。</param>
    /// <param name="minimumSize">至少需要的字节数。</param>
    public byte[] GetDataBlock(int dbNumber, int minimumSize = 0)
    {
        if (dbNumber <= 0)
        {
            throw new ZeusException("S7 DB 块号必须大于 0。");
        }

        var size = Math.Max(DefaultDataBlockSize, minimumSize);
        if (!_dataBlocks.TryGetValue(dbNumber, out var block))
        {
            block = new byte[size];
            _dataBlocks[dbNumber] = block;
            return block;
        }

        if (block.Length >= minimumSize)
        {
            return block;
        }

        Array.Resize(ref block, minimumSize);
        _dataBlocks[dbNumber] = block;
        return block;
    }
}
