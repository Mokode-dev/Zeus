namespace Zeus;

/// <summary>
/// Omron Host Link 常用字区。地址为协议内 0 基字地址。
/// </summary>
public enum HostLinkArea
{
    /// <summary>CIO / IR 区。</summary>
    Cio = 0,

    /// <summary>LR 链接区。</summary>
    Link = 1,

    /// <summary>HR 保持区。</summary>
    Holding = 2,

    /// <summary>AR 辅助区。</summary>
    Auxiliary = 3,

    /// <summary>DM 数据存储区。</summary>
    DataMemory = 4
}
