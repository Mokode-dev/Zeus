namespace Zeus;

/// <summary>
/// 把点历史转成图表控件常用的时间-数值样本。无法转成数值的采样会被跳过。
/// </summary>
public readonly record struct PointChartSample(DateTimeOffset Timestamp, double Value);

/// <summary>
/// 仪表盘绑定一次推送的当前值、报警与历史。
/// </summary>
public sealed class PointDashboardSnapshot
{
    /// <summary>
    /// 创建仪表盘快照。
    /// </summary>
    /// <param name="current">当前点快照。</param>
    /// <param name="history">最近成功采样。</param>
    /// <param name="chart">可直接喂给趋势图的数值样本。</param>
    public PointDashboardSnapshot(
        PointSnapshot current,
        IReadOnlyList<PointSnapshot> history,
        IReadOnlyList<PointChartSample> chart)
    {
        Current = current ?? throw new ArgumentNullException(nameof(current));
        History = history ?? throw new ArgumentNullException(nameof(history));
        Chart = chart ?? throw new ArgumentNullException(nameof(chart));
    }

    /// <summary>当前点快照，含报警与错误。</summary>
    public PointSnapshot Current { get; }

    /// <summary>最近成功采样历史。</summary>
    public IReadOnlyList<PointSnapshot> History { get; }

    /// <summary>趋势图样本，顺序从旧到新。</summary>
    public IReadOnlyList<PointChartSample> Chart { get; }

    /// <summary>最新可绘制数值；尚无数值时为 <c>null</c>。</summary>
    public double? LatestValue => Chart.Count == 0 ? null : Chart[^1].Value;

    /// <summary>当前是否处于高低报。</summary>
    public bool IsAlarmed => Current.IsAlarmed;
}

/// <summary>
/// 点历史到图表样本的转换。
/// </summary>
public static class PointChartFormatting
{
    /// <summary>
    /// 把成功采样转成带时间戳的数值点。无时间或无法转成数值的采样会被跳过。
    /// </summary>
    public static IReadOnlyList<PointChartSample> ToChartSamples(IEnumerable<PointSnapshot> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var samples = new List<PointChartSample>();
        foreach (var snapshot in history)
        {
            if (snapshot.UpdatedAt is null || !TryToDouble(snapshot.Value, out var value))
            {
                continue;
            }

            samples.Add(new PointChartSample(snapshot.UpdatedAt.Value, value));
        }

        return samples;
    }

    /// <summary>
    /// 尝试把工程值转成双精度，供仪表和趋势图使用。
    /// </summary>
    public static bool TryToDouble(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case double d:
                number = d;
                return true;
            case float f:
                number = f;
                return true;
            case IConvertible convertible:
                try
                {
                    number = convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception)
                {
                    number = 0;
                    return false;
                }
            default:
                return double.TryParse(
                    Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out number);
        }
    }
}
