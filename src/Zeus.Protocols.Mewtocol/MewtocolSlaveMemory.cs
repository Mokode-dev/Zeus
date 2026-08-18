namespace Zeus;

/// <summary>
/// Panasonic MEWTOCOL-COM 虚拟 PLC 的内存映像。地址均为协议里的 0 基字地址；位访问落在同一字表的 bit 0-15。
/// </summary>
public sealed class MewtocolSlaveMemory
{
    /// <summary>创建指定容量的映像。</summary>
    public MewtocolSlaveMemory(
        int externalInputWords = 10000,
        int externalOutputWords = 10000,
        int internalRelayWords = 10000,
        int linkRelayWords = 10000,
        int dataRegisterWords = 100000,
        int linkDataRegisterWords = 100000,
        int fileRegisterWords = 100000)
    {
        ValidateCapacity(externalInputWords, nameof(externalInputWords));
        ValidateCapacity(externalOutputWords, nameof(externalOutputWords));
        ValidateCapacity(internalRelayWords, nameof(internalRelayWords));
        ValidateCapacity(linkRelayWords, nameof(linkRelayWords));
        ValidateCapacity(dataRegisterWords, nameof(dataRegisterWords));
        ValidateCapacity(linkDataRegisterWords, nameof(linkDataRegisterWords));
        ValidateCapacity(fileRegisterWords, nameof(fileRegisterWords));

        ExternalInputWords = new ushort[externalInputWords];
        ExternalOutputWords = new ushort[externalOutputWords];
        InternalRelayWords = new ushort[internalRelayWords];
        LinkRelayWords = new ushort[linkRelayWords];
        DataRegisterWords = new ushort[dataRegisterWords];
        LinkDataRegisterWords = new ushort[linkDataRegisterWords];
        FileRegisterWords = new ushort[fileRegisterWords];
    }

    /// <summary>X 外部输入接点字。</summary>
    public ushort[] ExternalInputWords { get; }

    /// <summary>Y 外部输出接点字。</summary>
    public ushort[] ExternalOutputWords { get; }

    /// <summary>R 内部继电器接点字。</summary>
    public ushort[] InternalRelayWords { get; }

    /// <summary>L 链接继电器接点字。</summary>
    public ushort[] LinkRelayWords { get; }

    /// <summary>DT 数据寄存器。</summary>
    public ushort[] DataRegisterWords { get; }

    /// <summary>LD 链接数据寄存器。</summary>
    public ushort[] LinkDataRegisterWords { get; }

    /// <summary>FL 文件寄存器。</summary>
    public ushort[] FileRegisterWords { get; }

    private static void ValidateCapacity(int value, string name)
    {
        if (value <= 0)
        {
            throw new ZeusException($"MEWTOCOL 虚拟 PLC 内存容量 {name} 必须大于 0。");
        }
    }
}
