namespace Zeus;

/// <summary>
/// 在 Generic Host 构建完成后挂上 <see cref="IZeusHost"/>，供配置监视等后台服务回调运行时 API。
/// </summary>
internal sealed class ZeusHostAccessor
{
    /// <summary>已构建的宿主；构建完成前为 <c>null</c>。</summary>
    public IZeusHost? Host { get; set; }
}
