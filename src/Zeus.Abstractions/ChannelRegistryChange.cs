namespace Zeus;

/// <summary>
/// 通道目录的变更种类。
/// </summary>
public enum ChannelRegistryChange
{
    /// <summary>新通道已登记。</summary>
    Added = 0,

    /// <summary>通道已移除并关闭。</summary>
    Removed = 1
}
