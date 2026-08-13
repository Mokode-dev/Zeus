using System.Globalization;

namespace Zeus;

/// <summary>
/// 把点表变化封送到界面。与通道 <c>BindTo</c> 相同：释放句柄即退订。
/// </summary>
public static class PointTableUiExtensions
{
    /// <summary>
    /// 把指定点的值格式化为文本并推到界面。点尚无值时先推送空字符串。
    /// </summary>
    /// <param name="table">宿主点表。</param>
    /// <param name="pointName">短名或 <c>设备.点</c>。</param>
    /// <param name="dispatcher">界面调度器。</param>
    /// <param name="setText">在界面线程上设置文本。</param>
    /// <param name="formatter">可选格式化；默认使用不变区域性的 <see cref="object.ToString"/>。</param>
    /// <returns>绑定句柄。</returns>
    public static IUiBinding BindText(
        this IPointTable table,
        string pointName,
        IUiDispatcher dispatcher,
        Action<string> setText,
        Func<object?, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(setText);
        if (string.IsNullOrWhiteSpace(pointName))
        {
            throw new ZeusException("绑定点名不能为空。");
        }

        formatter ??= DefaultFormat;
        var key = pointName.Trim();

        void Apply(PointSnapshot snapshot)
        {
            var text = snapshot.Error is null ? formatter(snapshot.Value) : snapshot.Error;
            if (dispatcher.CheckAccess())
            {
                setText(text);
                return;
            }

            dispatcher.Post(() => setText(text));
        }

        void OnChanged(object? sender, PointChangedEventArgs e)
        {
            if (Matches(e.Current.Definition, key))
            {
                Apply(e.Current);
            }
        }

        if (table.All.FirstOrDefault(item => Matches(item.Definition, key)) is { } existing)
        {
            Apply(existing);
        }

        table.Changed += OnChanged;
        return new DelegateUiBinding(() => table.Changed -= OnChanged);
    }

    private static bool Matches(PointDefinition definition, string key)
    {
        return definition.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
               || definition.QualifiedName.Equals(key, StringComparison.OrdinalIgnoreCase);
    }

    private static string DefaultFormat(object? value)
    {
        return value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }
}
