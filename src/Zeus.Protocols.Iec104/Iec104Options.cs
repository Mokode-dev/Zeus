namespace Zeus;

/// <summary>
/// IEC 60870-5-104 会话选项。
/// </summary>
public sealed class Iec104Options
{
    /// <summary>公共地址，默认 1。</summary>
    public int CommonAddress { get; set; } = 1;

    /// <summary>传送原因中的源发地址，默认 0。</summary>
    public int OriginatorAddress { get; set; }

    /// <summary>总召唤限定词 QOI，默认 20，表示站总召唤。</summary>
    public int InterrogationQualifier { get; set; } = 20;
}
