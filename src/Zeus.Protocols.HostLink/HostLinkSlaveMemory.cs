namespace Zeus;

/// <summary>
/// Omron Host Link 虚拟 PLC 的内存映像。地址均为协议里的 0 基字地址；位访问落在同一字表的 bit 0–15。
/// </summary>
public sealed class HostLinkSlaveMemory
{
    /// <summary>创建指定容量的映像。</summary>
    public HostLinkSlaveMemory(
        int cioWords = 10000,
        int linkWords = 10000,
        int holdingWords = 10000,
        int auxiliaryWords = 10000,
        int dataMemoryWords = 10000)
    {
        ValidateCapacity(cioWords, nameof(cioWords));
        ValidateCapacity(linkWords, nameof(linkWords));
        ValidateCapacity(holdingWords, nameof(holdingWords));
        ValidateCapacity(auxiliaryWords, nameof(auxiliaryWords));
        ValidateCapacity(dataMemoryWords, nameof(dataMemoryWords));

        CioWords = new ushort[cioWords];
        LinkWords = new ushort[linkWords];
        HoldingWords = new ushort[holdingWords];
        AuxiliaryWords = new ushort[auxiliaryWords];
        DataMemoryWords = new ushort[dataMemoryWords];
    }

    /// <summary>CIO / IR 区。</summary>
    public ushort[] CioWords { get; }

    /// <summary>LR 链接区。</summary>
    public ushort[] LinkWords { get; }

    /// <summary>HR 保持区。</summary>
    public ushort[] HoldingWords { get; }

    /// <summary>AR 辅助区。</summary>
    public ushort[] AuxiliaryWords { get; }

    /// <summary>DM 数据存储区。</summary>
    public ushort[] DataMemoryWords { get; }

    private static void ValidateCapacity(int value, string name)
    {
        if (value <= 0)
        {
            throw new ZeusException($"Host Link 虚拟 PLC 内存容量 {name} 必须大于 0。");
        }
    }
}
