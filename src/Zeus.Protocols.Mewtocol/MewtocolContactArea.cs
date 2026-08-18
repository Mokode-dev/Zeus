namespace Zeus;

/// <summary>
/// Panasonic MEWTOCOL-COM 接点区。按 16 位字读写时地址为接点字地址。
/// </summary>
public enum MewtocolContactArea
{
    /// <summary>X 外部输入。</summary>
    ExternalInput = 0,

    /// <summary>Y 外部输出。</summary>
    ExternalOutput = 1,

    /// <summary>R 内部继电器。</summary>
    InternalRelay = 2,

    /// <summary>L 链接继电器。</summary>
    LinkRelay = 3
}
