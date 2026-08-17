namespace Zeus;

/// <summary>
/// FINS Memory Area Read/Write 使用的内存区代码。
/// 同一 Omron 区域通常有位代码与字代码两种，例如 CIO 位区 0x30、CIO 字区 0xB0。
/// </summary>
public readonly record struct FinsMemoryAreaCode(byte Code, FinsMemoryAreaKind Kind, string Name)
{
    /// <summary>CIO 位区。</summary>
    public static FinsMemoryAreaCode CioBit { get; } = new(0x30, FinsMemoryAreaKind.Bit, "CIO Bit");

    /// <summary>CIO 字区。</summary>
    public static FinsMemoryAreaCode CioWord { get; } = new(0xB0, FinsMemoryAreaKind.Word, "CIO Word");

    /// <summary>WR 工作位区。</summary>
    public static FinsMemoryAreaCode WorkBit { get; } = new(0x31, FinsMemoryAreaKind.Bit, "WR Bit");

    /// <summary>WR 工作字区。</summary>
    public static FinsMemoryAreaCode WorkWord { get; } = new(0xB1, FinsMemoryAreaKind.Word, "WR Word");

    /// <summary>HR 保持位区。</summary>
    public static FinsMemoryAreaCode HoldingBit { get; } = new(0x32, FinsMemoryAreaKind.Bit, "HR Bit");

    /// <summary>HR 保持字区。</summary>
    public static FinsMemoryAreaCode HoldingWord { get; } = new(0xB2, FinsMemoryAreaKind.Word, "HR Word");

    /// <summary>AR 辅助位区。</summary>
    public static FinsMemoryAreaCode AuxiliaryBit { get; } = new(0x33, FinsMemoryAreaKind.Bit, "AR Bit");

    /// <summary>AR 辅助字区。</summary>
    public static FinsMemoryAreaCode AuxiliaryWord { get; } = new(0xB3, FinsMemoryAreaKind.Word, "AR Word");

    /// <summary>DM 位区。</summary>
    public static FinsMemoryAreaCode DataMemoryBit { get; } = new(0x02, FinsMemoryAreaKind.Bit, "DM Bit");

    /// <summary>DM 字区。</summary>
    public static FinsMemoryAreaCode DataMemoryWord { get; } = new(0x82, FinsMemoryAreaKind.Word, "DM Word");

    /// <summary>TIM/CNT 完成标志位区。</summary>
    public static FinsMemoryAreaCode TimerCounterFlag { get; } = new(0x09, FinsMemoryAreaKind.Bit, "TIM/CNT Flag");

    /// <summary>TIM/CNT 当前值字区。</summary>
    public static FinsMemoryAreaCode TimerCounterValue { get; } = new(0x89, FinsMemoryAreaKind.Word, "TIM/CNT PV");

    /// <summary>当前 EM Bank 位区。</summary>
    public static FinsMemoryAreaCode CurrentEmBit { get; } = new(0x0A, FinsMemoryAreaKind.Bit, "Current EM Bit");

    /// <summary>当前 EM Bank 字区。</summary>
    public static FinsMemoryAreaCode CurrentEmWord { get; } = new(0x98, FinsMemoryAreaKind.Word, "Current EM Word");

    /// <summary>
    /// EM Bank 位区。支持 bank 0–18；0–15 使用 0x20–0x2F，16–18 使用 0xE0–0xE2。
    /// </summary>
    public static FinsMemoryAreaCode EmBankBit(int bank)
    {
        ValidateEmBank(bank);
        var code = bank <= 15 ? 0x20 + bank : 0xE0 + bank - 16;
        return new FinsMemoryAreaCode((byte)code, FinsMemoryAreaKind.Bit, $"EM{bank} Bit");
    }

    /// <summary>
    /// EM Bank 字区。支持 bank 0–18；0–15 使用 0xA0–0xAF，16–18 使用 0x60–0x62。
    /// </summary>
    public static FinsMemoryAreaCode EmBankWord(int bank)
    {
        ValidateEmBank(bank);
        var code = bank <= 15 ? 0xA0 + bank : 0x60 + bank - 16;
        return new FinsMemoryAreaCode((byte)code, FinsMemoryAreaKind.Word, $"EM{bank} Word");
    }

    /// <summary>是否为位区。</summary>
    public bool IsBit => Kind == FinsMemoryAreaKind.Bit;

    /// <summary>是否为字区。</summary>
    public bool IsWord => Kind == FinsMemoryAreaKind.Word;

    /// <inheritdoc />
    public override string ToString() => $"{Name} (0x{Code:X2})";

    private static void ValidateEmBank(int bank)
    {
        if (bank is < 0 or > 18)
        {
            throw new ZeusProtocolException($"FINS EM Bank 必须介于 0 与 18 之间，当前为 {bank}。");
        }
    }
}
