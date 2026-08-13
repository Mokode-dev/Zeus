namespace Zeus;

/// <summary>
/// 创建 Zeus 宿主的唯一推荐入口。
/// 用户只需在回调中声明通道与设备，启停顺序、日志与依赖注入由框架消化。
/// </summary>
public static class ZeusHost
{
    /// <summary>
    /// 按声明式配置创建宿主。
    /// </summary>
    /// <param name="configure">注册通道、设备或自定义服务。可为 <c>null</c>，此时得到空宿主。</param>
    /// <returns>尚未启动的宿主，调用 <see cref="IZeusHost.StartAsync"/> 后通道才会打开。</returns>
    public static IZeusHost Create(Action<ZeusHostBuilder>? configure = null)
    {
        var builder = new ZeusHostBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }
}
