using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 宿主构建器。用户在回调中注册通道、设备与自定义服务，框架负责组装 Generic Host。
/// </summary>
public sealed class ZeusHostBuilder
{
    private readonly HostApplicationBuilder _inner;
    private readonly List<Action<IServiceProvider, ChannelRegistry, DeviceRegistry>> _registrations = [];

    /// <summary>
    /// 由 <see cref="ZeusHost.Create"/> 创建，避免用户直接接触 Generic Host 细节。
    /// </summary>
    internal ZeusHostBuilder()
    {
        _inner = Host.CreateApplicationBuilder();
        _inner.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        Services.AddSingleton<ChannelRegistry>();
        Services.AddSingleton<IChannelRegistry>(sp => sp.GetRequiredService<ChannelRegistry>());
        Services.AddSingleton<DeviceRegistry>();
        Services.AddSingleton<IDeviceRegistry>(sp => sp.GetRequiredService<DeviceRegistry>());
        Services.AddSingleton(Acquisition);
        Services.AddSingleton<PointTable>();
        Services.AddSingleton<IPointTable>(sp => sp.GetRequiredService<PointTable>());
        Services.AddSingleton<IPointTableWriter>(sp => sp.GetRequiredService<PointTable>());
        Services.AddHostedService<ChannelLifecycleService>();
        Services.AddHostedService<AcquisitionLoopService>();
    }

    /// <summary>
    /// 采集选项单例。代码与 JSON 热更新都改这一份，循环每轮读取最新间隔。
    /// </summary>
    public AcquisitionOptions Acquisition { get; } = new();

    /// <summary>标准依赖注入容器。高级用户可在此注册自己的服务。</summary>
    public IServiceCollection Services => _inner.Services;

    /// <summary>
    /// 登记一个在构建完成后执行的通道/设备注册动作。
    /// 延迟到服务提供者就绪后再实例化通道，以便注入日志等依赖。
    /// </summary>
    /// <param name="registration">接收服务提供者与两个目录的回调。</param>
    public void Register(Action<IServiceProvider, ChannelRegistry, DeviceRegistry> registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registrations.Add(registration);
    }

    /// <summary>
    /// 构建不可变宿主。构建后不应再向本构建器注册通道。
    /// </summary>
    /// <returns>可启动的 <see cref="IZeusHost"/>。</returns>
    internal IZeusHost Build()
    {
        var host = _inner.Build();
        var channels = host.Services.GetRequiredService<ChannelRegistry>();
        var devices = host.Services.GetRequiredService<DeviceRegistry>();
        var points = host.Services.GetRequiredService<PointTable>();
        foreach (var registration in _registrations)
        {
            registration(host.Services, channels, devices);
        }

        return new ZeusHostRuntime(host, channels, devices, points);
    }
}
