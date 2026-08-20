using Microsoft.Extensions.Logging;

namespace Zeus;

/// <summary>
/// 给 <see cref="ILogger.BeginScope{TState}"/> 提供永不返回 <c>null</c> 的作用域。
/// 部分记录器的 BeginScope 可为 null，调用方仍应 <c>using</c> 释放。
/// </summary>
internal static class LogScope
{
    /// <summary>按单个结构化字段打开作用域。</summary>
    public static IDisposable Begin(ILogger logger, string key, object value)
        => logger.BeginScope(new Dictionary<string, object> { [key] = value }) ?? Null.Instance;

    /// <summary>按多个结构化字段打开作用域。</summary>
    public static IDisposable Begin(ILogger logger, IReadOnlyDictionary<string, object> values)
        => logger.BeginScope(values) ?? Null.Instance;

    private sealed class Null : IDisposable
    {
        public static readonly Null Instance = new();

        public void Dispose()
        {
        }
    }
}
