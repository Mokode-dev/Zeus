namespace Zeus;

/// <summary>
/// JSONL 点历史文件存储的选项。
/// </summary>
public sealed class FilePointHistoryStoreOptions
{
    /// <summary>历史文件路径。省略时由宿主扩展按进程工作目录生成。</summary>
    public string Path { get; set; } = "zeus-point-history.jsonl";
}
