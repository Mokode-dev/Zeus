namespace Zeus;

/// <summary>
/// 参数与状态守卫。集中抛出带建议动作的异常，避免各处消息风格不一致。
/// </summary>
internal static class Guard
{
    /// <summary>
    /// 确保字符串非空且不只含空白。
    /// </summary>
    /// <param name="value">待检查的值。</param>
    /// <param name="name">参数名。</param>
    /// <returns>去除首尾空白后的值。</returns>
    public static string NotNullOrWhiteSpace(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ZeusException($"参数 {name} 不能为空。请为通道或设备指定一个在宿主内唯一的名称。");
        }

        return value.Trim();
    }
}
