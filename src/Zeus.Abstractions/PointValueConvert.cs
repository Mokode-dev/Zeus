using System.Globalization;

namespace Zeus;

/// <summary>
/// 点表现值到常用 CLR 类型的转换。业务代码应优先走 <see cref="IPointTable.TryGetDouble"/>，
/// 而不是按 <see cref="ushort"/> / <see cref="double"/> 自行分支。
/// </summary>
public static class PointValueConvert
{
    /// <summary>
    /// 把点值转为有限双精度。布尔、字符、空值与非数值返回 <c>false</c>。
    /// </summary>
    /// <param name="value">点表现值。</param>
    /// <param name="number">成功时的数值。</param>
    public static bool TryToDouble(object? value, out double number)
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
