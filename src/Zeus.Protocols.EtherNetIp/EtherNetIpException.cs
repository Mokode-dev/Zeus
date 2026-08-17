namespace Zeus;

/// <summary>
/// EtherNet/IP 或 CIP 返回非成功状态时抛出的异常。
/// </summary>
public sealed class EtherNetIpException : ZeusProtocolException
{
    /// <summary>创建 EtherNet/IP 异常。</summary>
    public EtherNetIpException(string message, uint? encapsulationStatus = null, byte? generalStatus = null, IReadOnlyList<ushort>? additionalStatus = null)
        : base(message)
    {
        EncapsulationStatus = encapsulationStatus;
        GeneralStatus = generalStatus;
        AdditionalStatus = additionalStatus?.ToArray() ?? [];
    }

    /// <summary>封装层状态码，成功为 0。</summary>
    public uint? EncapsulationStatus { get; }

    /// <summary>CIP General Status，成功为 0。</summary>
    public byte? GeneralStatus { get; }

    /// <summary>CIP 附加状态字。</summary>
    public IReadOnlyList<ushort> AdditionalStatus { get; }
}
