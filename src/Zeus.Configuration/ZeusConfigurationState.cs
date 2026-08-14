namespace Zeus;

/// <summary>
/// 记录最近一次成功装载的 JSON 配置，供热更新做差异比较。
/// </summary>
internal sealed class ZeusConfigurationState
{
    /// <summary>AddJsonFile 登记的绝对路径；仅内存 JSON 时为 <c>null</c>。</summary>
    public string? Path { get; set; }

    /// <summary>上一份已应用到宿主的配置。尚未装载时为空文档。</summary>
    public ZeusAppConfiguration Last { get; set; } = new();
}
