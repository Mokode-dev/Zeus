using Microsoft.Extensions.Configuration;
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
        Services.AddSingleton<ChannelRegistry>();
        Services.AddSingleton<IChannelRegistry>(sp => sp.GetRequiredService<ChannelRegistry>());
        Services.AddSingleton<DeviceRegistry>();
        Services.AddSingleton<IDeviceRegistry>(sp => sp.GetRequiredService<DeviceRegistry>());
        Services.AddSingleton(Acquisition);
        Services.AddSingleton(Reconnect);
        Services.AddSingleton<HostRunState>();
        Services.AddSingleton<ZeusHostAccessor>();
        Services.AddSingleton(sp => new PointTable(
            sp.GetRequiredService<DeviceRegistry>(),
            historyCapacity: 128,
            maxHistoryPoints: 4096,
            store: sp.GetService<IPointHistoryStore>(),
            logger: sp.GetService<ILogger<PointTable>>()));
        Services.AddSingleton<IPointTable>(sp => sp.GetRequiredService<PointTable>());
        Services.AddSingleton<IPointTableWriter>(sp => sp.GetRequiredService<PointTable>());
        Services.AddSingleton(sp => new PointAlarmTable(sp.GetRequiredService<PointTable>()));
        Services.AddSingleton<IPointAlarmTable>(sp => sp.GetRequiredService<PointAlarmTable>());
        Services.AddSingleton<ChannelSubscriptionMigrator>();
        Services.AddHostedService<ChannelLifecycleService>();
        Services.AddHostedService<AcquisitionLoopService>();
        Services.AddHostedService<ChannelReconnectService>();
    }

    /// <summary>
    /// 采集选项单例。代码与 JSON 热更新都改这一份，循环每轮读取最新间隔。
    /// </summary>
    public AcquisitionOptions Acquisition { get; } = new();

    /// <summary>
    /// 通道故障自动重连选项。与采集选项一样是单例，运行中修改下一轮退避即生效。
    /// </summary>
    public ChannelReconnectOptions Reconnect { get; } = new();

    /// <summary>标准依赖注入容器。高级用户可在此注册自己的服务。</summary>
    public IServiceCollection Services => _inner.Services;

    /// <summary>
    /// 日志构建器。可接入 JSON Console、文件、Serilog 或按类别过滤；默认沿用 Generic Host 的控制台记录器。
    /// </summary>
    public ILoggingBuilder Logging => _inner.Logging;

    /// <summary>
    /// 配置管理器。可追加 <c>appsettings.json</c> 或环境变量，供日志级别等宿主配置使用。
    /// </summary>
    public ConfigurationManager Configuration => _inner.Configuration;

    /// <summary>宿主环境，包含内容根目录与环境名。</summary>
    public IHostEnvironment Environment => _inner.Environment;

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
        var alarms = host.Services.GetRequiredService<PointAlarmTable>();
        var runState = host.Services.GetRequiredService<HostRunState>();
        // 通信日志必须先于订阅迁移器订阅目录变更：热重载移除旧通道时先退订报文日志，避免处理器被迁到新实例后重复挂接。
        // 构建期通道随后走 Added 事件，因此本服务仍能挂上 Create 回调里登记的通道。
        _ = host.Services.GetService<ChannelCommunicationLogService>();
        _ = host.Services.GetRequiredService<ChannelSubscriptionMigrator>();
        foreach (var registration in _registrations)
        {
            registration(host.Services, channels, devices);
        }

        var runtime = new ZeusHostRuntime(host, channels, devices, points, alarms, runState);
        host.Services.GetRequiredService<ZeusHostAccessor>().Host = runtime;
        return runtime;
    }
}
