using System.Globalization;

namespace Zeus;

/// <summary>
/// 点表报警限。低于下限触发低报，高于上限触发高报；等于阈值仍视为正常。
/// </summary>
public sealed class PointAlarmLimits
{
    /// <summary>
    /// 创建报警限。至少需要提供一个阈值。
    /// </summary>
    /// <param name="low">低报阈值。值低于该阈值时为 <see cref="PointAlarmState.Low"/>。</param>
    /// <param name="high">高报阈值。值高于该阈值时为 <see cref="PointAlarmState.High"/>。</param>
    public PointAlarmLimits(double? low = null, double? high = null)
    {
        if (low is null && high is null)
        {
            throw new ZeusException("报警限至少需要配置 low 或 high 中的一个。");
        }

        ValidateFinite(low, nameof(low));
        ValidateFinite(high, nameof(high));
        if (low > high)
        {
            throw new ZeusException("报警下限不能高于报警上限。");
        }

        Low = low;
        High = high;
    }

    /// <summary>低报阈值。</summary>
    public double? Low { get; }

    /// <summary>高报阈值。</summary>
    public double? High { get; }

    /// <summary>
    /// 根据当前点值判断报警状态。
    /// </summary>
    /// <param name="value">当前点值。</param>
    public PointAlarmState Evaluate(object? value)
    {
        if (!TryReadNumber(value, out var number))
        {
            return PointAlarmState.Unknown;
        }

        if (Low is { } low && number < low)
        {
            return PointAlarmState.Low;
        }

        if (High is { } high && number > high)
        {
            return PointAlarmState.High;
        }

        return PointAlarmState.Normal;
    }

    private static void ValidateFinite(double? value, string name)
    {
        if (value is { } number && !double.IsFinite(number))
        {
            throw new ZeusException($"报警限 {name} 必须是有限数值。");
        }
    }

    private static bool TryReadNumber(object? value, out double number)
    {
        number = 0;
        if (value is null or bool or char)
        {
            return false;
        }

        try
        {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(number);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
