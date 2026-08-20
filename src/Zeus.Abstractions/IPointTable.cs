namespace Zeus;

/// <summary>
/// 宿主级点表。采集循环写入快照；界面与业务按名称读取，也可对可写点调用 <see cref="WriteAsync"/>。
/// 短名在全宿主唯一时可直接使用；否则请用 <c>设备.点</c> 限定名。
/// </summary>
public interface IPointTable
{
    /// <summary>当前全部点的快照，顺序与登记顺序一致。</summary>
    IReadOnlyList<PointSnapshot> All { get; }

    /// <summary>点的值或错误发生变化时触发。可能在采集线程上发出。</summary>
    event EventHandler<PointChangedEventArgs>? Changed;

    /// <summary>
    /// 按短名或限定名获取快照。
    /// </summary>
    /// <param name="name">点名或 <c>设备.点</c>。</param>
    PointSnapshot Get(string name);

    /// <summary>
    /// 按短名或限定名获取并转换为 <typeparamref name="T"/>。
    /// </summary>
    /// <typeparam name="T">期望的 CLR 类型，例如 <see cref="ushort"/>、<see cref="bool"/>、<see cref="double"/>。</typeparam>
    /// <param name="name">点名或限定名。</param>
    T Get<T>(string name);

    /// <summary>
    /// 尝试读取并转换。点不存在、尚无值或类型不匹配时返回 <c>false</c>。
    /// </summary>
    /// <typeparam name="T">期望类型。</typeparam>
    /// <param name="name">点名或限定名。</param>
    /// <param name="value">成功时的值。</param>
    bool TryGet<T>(string name, out T? value);

    /// <summary>
    /// 读取指定点最近的成功采样历史，顺序从旧到新。错误采集不会写入历史。
    /// 若登记了 <see cref="IPointHistoryStore"/>，此处仍返回内存环形缓冲；持久化历史请直接查存储。
    /// </summary>
    /// <param name="name">点名或限定名。</param>
    IReadOnlyList<PointSnapshot> GetHistory(string name);

    /// <summary>
    /// 按点名把工程值写回所属设备。界面与业务应走这条路径，而不是直接拼寄存器地址。
    /// 点必须声明为可写，且所属设备实现 <see cref="IPointWriter"/>。
    /// </summary>
    /// <param name="name">短名或 <c>设备.点</c>。</param>
    /// <param name="value">工程值。带 <c>scale</c> 的寄存器点传入换算后的值，例如 <c>80.0</c>。</param>
    /// <param name="cancellationToken">取消本次写入。</param>
    Task WriteAsync(string name, object value, CancellationToken cancellationToken = default);
}
