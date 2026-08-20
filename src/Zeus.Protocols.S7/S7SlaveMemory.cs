namespace Zeus;

/// <summary>
/// Siemens S7 虚拟 PLC 的内存映像。
/// </summary>
public sealed class S7SlaveMemory
{
    private readonly Dictionary<int, byte[]> _dataBlocks = new();

    /// <summary>单个 DB 默认最大字节数。</summary>
    public const int DefaultMaxDataBlockSize = 65536;

    /// <summary>默认允许创建的 DB 块数量上限。</summary>
    public const int DefaultMaxDataBlockCount = 256;

    /// <summary>
    /// 创建指定容量的映像。
    /// </summary>
    /// <param name="inputs">输入区 I 字节数。</param>
    /// <param name="outputs">输出区 Q 字节数。</param>
    /// <param name="markers">标志区 M 字节数。</param>
    /// <param name="defaultDataBlockSize">自动创建 DB 时的默认字节数。</param>
    public S7SlaveMemory(int inputs = 1024, int outputs = 1024, int markers = 4096, int defaultDataBlockSize = 4096)
        : this(inputs, outputs, markers, defaultDataBlockSize, DefaultMaxDataBlockSize, DefaultMaxDataBlockCount)
    {
    }

    /// <summary>
    /// 创建指定容量的映像，并限制单个 DB 与 DB 数量，避免按请求无限扩容。
    /// </summary>
    public S7SlaveMemory(
        int inputs,
        int outputs,
        int markers,
        int defaultDataBlockSize,
        int maxDataBlockSize,
        int maxDataBlockCount)
    {
        if (inputs < 0 || outputs < 0 || markers < 0 || defaultDataBlockSize <= 0)
        {
            throw new ZeusException("S7 虚拟 PLC 内存容量不能为负数，默认 DB 容量必须大于 0。");
        }

        if (maxDataBlockSize < defaultDataBlockSize || maxDataBlockCount <= 0)
        {
            throw new ZeusException("S7 虚拟 PLC 的 DB 上限必须大于默认容量，且块数必须大于 0。");
        }

        Inputs = new byte[inputs];
        Outputs = new byte[outputs];
        Markers = new byte[markers];
        DefaultDataBlockSize = defaultDataBlockSize;
        MaxDataBlockSize = maxDataBlockSize;
        MaxDataBlockCount = maxDataBlockCount;
    }

    /// <summary>输入区 I。</summary>
    public byte[] Inputs { get; }

    /// <summary>输出区 Q。</summary>
    public byte[] Outputs { get; }

    /// <summary>标志区 M。</summary>
    public byte[] Markers { get; }

    /// <summary>自动创建 DB 时的默认容量。</summary>
    public int DefaultDataBlockSize { get; }

    /// <summary>单个 DB 允许的最大字节数。</summary>
    public int MaxDataBlockSize { get; }

    /// <summary>允许创建的 DB 块数量上限。</summary>
    public int MaxDataBlockCount { get; }

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

        if (minimumSize > MaxDataBlockSize)
        {
            throw new ZeusException($"S7 DB{dbNumber} 请求 {minimumSize} 字节，超过上限 {MaxDataBlockSize}。");
        }

        var size = Math.Max(DefaultDataBlockSize, minimumSize);
        if (!_dataBlocks.TryGetValue(dbNumber, out var block))
        {
            if (_dataBlocks.Count >= MaxDataBlockCount)
            {
                throw new ZeusException($"S7 虚拟 PLC 已达到 DB 数量上限 {MaxDataBlockCount}。");
            }

            block = new byte[size];
            _dataBlocks[dbNumber] = block;
            return block;
        }

        if (block.Length >= minimumSize)
        {
            return block;
        }

        Array.Resize(ref block, Math.Min(size, MaxDataBlockSize));
        _dataBlocks[dbNumber] = block;
        return block;
    }
}
