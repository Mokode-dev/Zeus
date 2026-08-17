using System.Globalization;

namespace Zeus;

/// <summary>
/// 点表界面绑定的内部格式化与匹配规则，保持 WinForms / WPF 适配器行为一致。
/// </summary>
internal static class PointUiFormatting
{
    public static string NormalizePointName(string pointName)
    {
        if (string.IsNullOrWhiteSpace(pointName))
        {
            throw new ZeusException("绑定点名不能为空。");
        }

        return pointName.Trim();
    }

    public static bool Matches(PointDefinition definition, string key)
    {
        return definition.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
               || definition.QualifiedName.Equals(key, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatSnapshot(PointSnapshot snapshot, Func<object?, string> formatter)
    {
        return snapshot.Error is null ? formatter(snapshot.Value) : snapshot.Error;
    }

    public static string DefaultFormat(object? value)
    {
        return value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }
}
