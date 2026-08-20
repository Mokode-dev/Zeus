using System.Globalization;

namespace Zeus;

/// <summary>
/// 线程安全的内存点表。采集循环写入快照；业务与界面按名称读取，也可按名称写回设备。
/// </summary>
public sealed class PointTable : IPointTable, IPointTableWriter
{
    private const int DefaultHistoryCapacity = 128;
    private const int DefaultMaxHistoryPoints = 4096;

    private readonly object _gate = new();
    private readonly int _historyCapacity;
    private readonly int _maxHistoryPoints;
    private readonly IDeviceRegistry? _devices;
    private readonly IPointHistoryStore? _store;
    private readonly List<string> _order = [];
    private readonly Dictionary<string, PointSnapshot> _byQualified = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<PointSnapshot>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _shortToQualified = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ambiguousShortNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 创建未连接设备目录的点表。可以读写快照，但不能 <see cref="WriteAsync"/>。
    /// </summary>
    /// <param name="historyCapacity">每个点保留的最近成功采样数。设为 0 可关闭历史缓冲。</param>
    public PointTable(int historyCapacity = DefaultHistoryCapacity)
        : this(null, historyCapacity)
    {
    }

    /// <summary>
    /// 创建连接到设备目录的点表。宿主通过本构造函数注入目录，以便按点名路由写回。
    /// </summary>
    /// <param name="devices">设备目录。为 <c>null</c> 时禁止写回。</param>
    /// <param name="historyCapacity">每个点保留的最近成功采样数。设为 0 可关闭历史缓冲。</param>
    public PointTable(IDeviceRegistry? devices, int historyCapacity = DefaultHistoryCapacity)
        : this(devices, historyCapacity, DefaultMaxHistoryPoints)
    {
    }

    /// <summary>
    /// 创建连接到设备目录的点表，并限制历史点数量，避免上千点时内存无界增长。
    /// </summary>
    /// <param name="devices">设备目录。为 <c>null</c> 时禁止写回。</param>
    /// <param name="historyCapacity">每个点保留的最近成功采样数。设为 0 可关闭历史缓冲。</param>
    /// <param name="maxHistoryPoints">允许保留历史的最大点数；超出后新点不再记历史。</param>
    public PointTable(IDeviceRegistry? devices, int historyCapacity, int maxHistoryPoints)
        : this(devices, historyCapacity, maxHistoryPoints, null)
    {
    }

    /// <summary>
    /// 创建连接到设备目录的点表，并可把成功采样交给可插拔存储。
    /// </summary>
    /// <param name="devices">设备目录。为 <c>null</c> 时禁止写回。</param>
    /// <param name="historyCapacity">每个点保留的最近成功采样数。设为 0 可关闭历史缓冲。</param>
    /// <param name="maxHistoryPoints">允许保留历史的最大点数；超出后新点不再记历史。</param>
    /// <param name="store">可选落盘。为 <c>null</c> 时只保留内存历史。</param>
    public PointTable(
        IDeviceRegistry? devices,
        int historyCapacity,
        int maxHistoryPoints,
        IPointHistoryStore? store)
    {
        if (historyCapacity < 0)
        {
            throw new ZeusException("点表历史容量不能为负数。");
        }

        if (maxHistoryPoints < 0)
        {
            throw new ZeusException("点表历史点数上限不能为负数。");
        }

        _devices = devices;
        _historyCapacity = historyCapacity;
        _maxHistoryPoints = maxHistoryPoints;
        _store = store;
    }

    /// <summary>每个点最多保留的最近成功采样数。</summary>
    public int HistoryCapacity => _historyCapacity;

    /// <inheritdoc />
    public event EventHandler<PointChangedEventArgs>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<PointSnapshot> All
    {
        get
        {
            lock (_gate)
            {
                return _order.Select(name => _byQualified[name]).ToArray();
            }
        }
    }

    /// <inheritdoc />
    public PointSnapshot Get(string name)
    {
        if (!TryGetSnapshot(name, out var snapshot) || snapshot is null)
        {
            throw CreateMissingException(name);
        }

        return snapshot;
    }

    /// <inheritdoc />
    public T Get<T>(string name)
    {
        var snapshot = Get(name);
        if (snapshot.Value is null)
        {
            throw new ZeusException(
                $"点 {snapshot.QualifiedName} 尚无有效值。请等待采集循环完成第一轮，或检查 Error：{snapshot.Error ?? "无"}。");
        }

        if (TryConvert(snapshot.Value, out T? typed) && typed is not null)
        {
            return typed;
        }

        throw new ZeusException(
            $"点 {snapshot.QualifiedName} 的实际类型为 {snapshot.Value.GetType().Name}，无法作为 {typeof(T).Name} 读取。");
    }

    /// <inheritdoc />
    public bool TryGet<T>(string name, out T? value)
    {
        value = default;
        if (!TryGetSnapshot(name, out var snapshot) || snapshot?.Value is null)
        {
            return false;
        }

        return TryConvert(snapshot.Value, out value);
    }

    /// <inheritdoc />
    public IReadOnlyList<PointSnapshot> GetHistory(string name)
    {
        if (!TryResolveQualifiedName(name, out var qualifiedName) || qualifiedName is null)
        {
            throw CreateMissingException(name);
        }

        lock (_gate)
        {
            return _history.TryGetValue(qualifiedName, out var items)
                ? items.ToArray()
                : Array.Empty<PointSnapshot>();
        }
    }

    /// <inheritdoc />
    public void Register(PointDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_gate)
        {
            if (_byQualified.ContainsKey(definition.QualifiedName))
            {
                throw new ZeusException(
                    $"点 {definition.QualifiedName} 已存在。同一设备内点名必须唯一。");
            }

            _byQualified[definition.QualifiedName] = new PointSnapshot(definition, null, null, null);
            _history[definition.QualifiedName] = [];
            _order.Add(definition.QualifiedName);

            if (_ambiguousShortNames.Contains(definition.Name))
            {
                return;
            }

            if (_shortToQualified.ContainsKey(definition.Name))
            {
                _shortToQualified.Remove(definition.Name);
                _ambiguousShortNames.Add(definition.Name);
            }
            else
            {
                _shortToQualified[definition.Name] = definition.QualifiedName;
            }
        }
    }

    /// <inheritdoc />
    public void Publish(string qualifiedName, object? value)
    {
        PointSnapshot? previous;
        PointSnapshot current;
        lock (_gate)
        {
            if (!_byQualified.TryGetValue(qualifiedName, out var existing))
            {
                throw new ZeusException($"无法写入未登记的点 {qualifiedName}。请先在设备上声明点表。");
            }

            if (existing.Error is null && Equals(existing.Value, value))
            {
                return;
            }

            previous = existing;
            current = new PointSnapshot(existing.Definition, value, DateTimeOffset.Now, null);
            _byQualified[qualifiedName] = current;
            AddHistory(current);
        }

        PersistHistory(current);
        Changed?.Invoke(this, new PointChangedEventArgs(previous, current));
    }

    /// <inheritdoc />
    public void PublishError(string qualifiedName, string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ZeusException("采集错误说明不能为空。");
        }

        PointSnapshot? previous;
        PointSnapshot current;
        lock (_gate)
        {
            if (!_byQualified.TryGetValue(qualifiedName, out var existing))
            {
                throw new ZeusException($"无法写入未登记的点 {qualifiedName}。请先在设备上声明点表。");
            }

            if (string.Equals(existing.Error, error, StringComparison.Ordinal))
            {
                return;
            }

            previous = existing;
            current = new PointSnapshot(existing.Definition, existing.Value, existing.UpdatedAt, error);
            _byQualified[qualifiedName] = current;
        }

        Changed?.Invoke(this, new PointChangedEventArgs(previous, current));
    }

    /// <inheritdoc />
    public void Unregister(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return;
        }

        lock (_gate)
        {
            RemoveLocked(qualifiedName.Trim());
        }
    }

    /// <inheritdoc />
    public void UnregisterDevice(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return;
        }

        var key = deviceName.Trim();
        lock (_gate)
        {
            var doomed = _order
                .Where(name => _byQualified.TryGetValue(name, out var snapshot)
                    && string.Equals(snapshot.Definition.DeviceName, key, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var qualified in doomed)
            {
                RemoveLocked(qualified);
            }
        }
    }

    /// <summary>
    /// 在已持有锁的前提下摘除一个点，并重建短名索引。
    /// </summary>
    private void RemoveLocked(string qualifiedName)
    {
        if (!_byQualified.Remove(qualifiedName, out var snapshot))
        {
            return;
        }

        _history.Remove(qualifiedName);
        _order.Remove(qualifiedName);
        RebuildShortNameIndex(snapshot.Definition.Name);
    }

    /// <summary>
    /// 某个短名对应的点被移除后，按剩余点重建短名到限定名的映射。
    /// </summary>
    private void RebuildShortNameIndex(string shortName)
    {
        _shortToQualified.Remove(shortName);
        _ambiguousShortNames.Remove(shortName);

        string? unique = null;
        foreach (var name in _order)
        {
            if (!_byQualified.TryGetValue(name, out var snapshot)
                || !string.Equals(snapshot.Definition.Name, shortName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (unique is not null)
            {
                unique = null;
                _ambiguousShortNames.Add(shortName);
                return;
            }

            unique = name;
        }

        if (unique is not null)
        {
            _shortToQualified[shortName] = unique;
        }
    }

    private void AddHistory(PointSnapshot snapshot)
    {
        if (_historyCapacity == 0)
        {
            return;
        }

        if (!_history.TryGetValue(snapshot.QualifiedName, out var items))
        {
            if (_history.Count >= _maxHistoryPoints)
            {
                return;
            }

            items = [];
            _history[snapshot.QualifiedName] = items;
        }

        items.Add(snapshot);
        if (items.Count > _historyCapacity)
        {
            items.RemoveRange(0, items.Count - _historyCapacity);
        }
    }

    /// <summary>
    /// 把成功采样交给可插拔存储。失败不得打断采集循环。
    /// </summary>
    private void PersistHistory(PointSnapshot snapshot)
    {
        if (_store is null)
        {
            return;
        }

        _ = PersistHistoryAsync(snapshot);
    }

    private async Task PersistHistoryAsync(PointSnapshot snapshot)
    {
        try
        {
            await _store!.AppendAsync(snapshot).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 落盘失败只影响持久化，点表现值和内存历史仍可用。
        }
    }

    private bool TryGetSnapshot(string name, out PointSnapshot? snapshot)
    {
        snapshot = null;
        if (!TryResolveQualifiedName(name, out var qualifiedName) || qualifiedName is null)
        {
            return false;
        }

        lock (_gate)
        {
            return _byQualified.TryGetValue(qualifiedName, out snapshot);
        }
    }

    private bool TryResolveQualifiedName(string name, out string? qualifiedName)
    {
        qualifiedName = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var key = name.Trim();
        lock (_gate)
        {
            if (_byQualified.ContainsKey(key))
            {
                qualifiedName = key;
                return true;
            }

            if (_ambiguousShortNames.Contains(key))
            {
                throw new ZeusException(
                    $"点名 {key} 在多台设备上重复。请使用限定名，例如 oven.{key}。");
            }

            if (_shortToQualified.TryGetValue(key, out var qualified)
                && _byQualified.ContainsKey(qualified))
            {
                qualifiedName = qualified;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task WriteAsync(string name, object value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!TryResolveQualifiedName(name, out var qualifiedName) || qualifiedName is null)
        {
            throw CreateMissingException(name);
        }

        PointSnapshot snapshot;
        lock (_gate)
        {
            snapshot = _byQualified[qualifiedName];
        }

        var definition = snapshot.Definition;
        if (!definition.Writable)
        {
            throw new ZeusException(
                $"点 {definition.QualifiedName} 是只读点，不能写回。请在声明时把该点标为可写，例如 HoldingRegister(\"{definition.Name}\", address, writable: true)。");
        }

        if (_devices is null)
        {
            throw new ZeusException(
                $"点表未连接到设备目录，无法写回 {definition.QualifiedName}。请通过 ZeusHost 使用点表，而不是单独 new PointTable。");
        }

        if (!_devices.TryGet<IDevice>(definition.DeviceName, out var device) || device is null)
        {
            throw new ZeusException($"点 {definition.QualifiedName} 所属设备 {definition.DeviceName} 已不存在。");
        }

        if (device is not IPointWriter writer)
        {
            throw new ZeusException(
                $"设备 {device.Name}（{device.GetType().Name}）未实现 IPointWriter，不能按点名写回。自定义设备请实现该接口。");
        }

        try
        {
            await writer.WriteAsync(definition.Name, value, this, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ZeusException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 设备未按约定写入错误快照时，点表仍补上 Error，避免界面只看到异常、看不到点状态。
            PublishError(definition.QualifiedName, ex.Message);
            throw;
        }
    }

    private ZeusException CreateMissingException(string name)
    {
        lock (_gate)
        {
            var available = _order.Count == 0
                ? "当前尚未登记任何点"
                : "已登记：" + string.Join("、", _order);
            return new ZeusException($"找不到名为 {name} 的点。{available}。");
        }
    }

    private static bool TryConvert<T>(object value, out T? typed)
    {
        if (value is T direct)
        {
            typed = direct;
            return true;
        }

        try
        {
            typed = (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception)
        {
            typed = default;
            return false;
        }
    }
}
