using System.Globalization;

namespace Zeus;

/// <summary>
/// 线程安全的内存点表。采集循环写入，业务与界面只读查找。
/// </summary>
public sealed class PointTable : IPointTable, IPointTableWriter
{
    private readonly object _gate = new();
    private readonly List<string> _order = [];
    private readonly Dictionary<string, PointSnapshot> _byQualified = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _shortToQualified = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ambiguousShortNames = new(StringComparer.OrdinalIgnoreCase);

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
        }

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

    private bool TryGetSnapshot(string name, out PointSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var key = name.Trim();
        lock (_gate)
        {
            if (_byQualified.TryGetValue(key, out snapshot))
            {
                return true;
            }

            if (_ambiguousShortNames.Contains(key))
            {
                throw new ZeusException(
                    $"点名 {key} 在多台设备上重复。请使用限定名，例如 oven.{key}。");
            }

            if (_shortToQualified.TryGetValue(key, out var qualified)
                && _byQualified.TryGetValue(qualified, out snapshot))
            {
                return true;
            }
        }

        return false;
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
