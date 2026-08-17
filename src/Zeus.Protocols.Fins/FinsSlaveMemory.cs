namespace Zeus;

/// <summary>
/// Omron FINS 虚拟 PLC 的内存映像。地址均为协议里的 0 基字地址；位访问落在同一字表的 bit 0–15。
/// </summary>
public sealed class FinsSlaveMemory
{
    private readonly ushort[][] _emBanks;

    /// <summary>创建指定容量的映像。</summary>
    public FinsSlaveMemory(
        int cioWords = 4096,
        int workWords = 4096,
        int holdingWords = 4096,
        int auxiliaryWords = 1024,
        int dataMemoryWords = 32768,
        int timerCounterWords = 4096,
        int currentEmWords = 32768,
        int emBankWords = 32768)
    {
        ValidateCapacity(cioWords, nameof(cioWords));
        ValidateCapacity(workWords, nameof(workWords));
        ValidateCapacity(holdingWords, nameof(holdingWords));
        ValidateCapacity(auxiliaryWords, nameof(auxiliaryWords));
        ValidateCapacity(dataMemoryWords, nameof(dataMemoryWords));
        ValidateCapacity(timerCounterWords, nameof(timerCounterWords));
        ValidateCapacity(currentEmWords, nameof(currentEmWords));
        ValidateCapacity(emBankWords, nameof(emBankWords));

        CioWords = new ushort[cioWords];
        WorkWords = new ushort[workWords];
        HoldingWords = new ushort[holdingWords];
        AuxiliaryWords = new ushort[auxiliaryWords];
        DataMemoryWords = new ushort[dataMemoryWords];
        TimerCounterValues = new ushort[timerCounterWords];
        TimerCounterFlags = new bool[timerCounterWords * 16];
        CurrentEmWords = new ushort[currentEmWords];
        _emBanks = Enumerable.Range(0, 19).Select(_ => new ushort[emBankWords]).ToArray();
    }

    /// <summary>CIO 区。</summary>
    public ushort[] CioWords { get; }

    /// <summary>WR 工作区。</summary>
    public ushort[] WorkWords { get; }

    /// <summary>HR 保持区。</summary>
    public ushort[] HoldingWords { get; }

    /// <summary>AR 辅助区。</summary>
    public ushort[] AuxiliaryWords { get; }

    /// <summary>DM 数据存储区。</summary>
    public ushort[] DataMemoryWords { get; }

    /// <summary>TIM/CNT 当前值。</summary>
    public ushort[] TimerCounterValues { get; }

    /// <summary>TIM/CNT 完成标志。</summary>
    public bool[] TimerCounterFlags { get; }

    /// <summary>当前 EM Bank。</summary>
    public ushort[] CurrentEmWords { get; }

    /// <summary>获取 EM Bank 0–18 的字表。</summary>
    public ushort[] GetEmBank(int bank)
    {
        if (bank is < 0 or > 18)
        {
            throw new ZeusException($"FINS EM Bank 必须介于 0 与 18 之间，当前为 {bank}。");
        }

        return _emBanks[bank];
    }

    private static void ValidateCapacity(int value, string name)
    {
        if (value <= 0)
        {
            throw new ZeusException($"FINS 虚拟 PLC 内存容量 {name} 必须大于 0。");
        }
    }
}
