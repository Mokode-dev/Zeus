using System.Reflection;

namespace Zeus;

/// <summary>
/// JSON 协议绑定目录。协议程序集通过模块初始化器登记；装载配置前会探测输出目录中的官方协议包。
/// </summary>
public static class ZeusJsonBinders
{
    private static readonly object Gate = new();
    private static readonly List<IZeusJsonBinder> Binders = [];
    private static readonly string[] WellKnownAssemblies =
    [
        "Zeus.Protocols.Modbus",
        "Zeus.Protocols.Mc",
        "Zeus.Protocols.S7",
        "Zeus.Protocols.Fins",
        "Zeus.Protocols.HostLink",
        "Zeus.Protocols.Mewtocol",
        "Zeus.Protocols.EtherNetIp",
        "Zeus.Protocols.Dlt645",
        "Zeus.Protocols.Iec104",
        "Zeus.Protocols.Mqtt",
        "Zeus.Protocols.Snmp"
    ];

    /// <summary>
    /// 登记一个协议绑定。重复登记同一实例会被忽略。
    /// </summary>
    /// <param name="binder">协议绑定。</param>
    public static void Register(IZeusJsonBinder binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        lock (Gate)
        {
            if (Binders.Any(existing => ReferenceEquals(existing, binder) || existing.GetType() == binder.GetType()))
            {
                return;
            }

            Binders.Add(binder);
        }
    }

    /// <summary>
    /// 探测已加载及输出目录中的官方协议程序集，触发其模块初始化器。
    /// </summary>
    public static void Probe()
    {
        foreach (var name in WellKnownAssemblies)
        {
            try
            {
                var assembly = Assembly.Load(name);
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (!typeof(IZeusJsonBinder).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
                    {
                        continue;
                    }

                    Register((IZeusJsonBinder)Activator.CreateInstance(type)!);
                }
            }
            catch (Exception)
            {
                // 未引用的协议包不在输出目录，忽略。
            }
        }
    }

    /// <summary>
    /// 按设备类型查找绑定。
    /// </summary>
    public static IZeusJsonBinder? FindDevice(string normalizedType)
    {
        Probe();
        lock (Gate)
        {
            return Binders.FirstOrDefault(binder =>
                binder.DeviceTypes.Any(type => string.Equals(type, normalizedType, StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>
    /// 按虚拟从站类型查找绑定。
    /// </summary>
    public static IZeusJsonBinder? FindResponder(string normalizedResponder)
    {
        Probe();
        lock (Gate)
        {
            return Binders.FirstOrDefault(binder =>
                binder.ResponderTypes.Any(type => string.Equals(type, normalizedResponder, StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>当前已登记绑定的快照。</summary>
    public static IReadOnlyList<IZeusJsonBinder> All
    {
        get
        {
            Probe();
            lock (Gate)
            {
                return Binders.ToArray();
            }
        }
    }
}
