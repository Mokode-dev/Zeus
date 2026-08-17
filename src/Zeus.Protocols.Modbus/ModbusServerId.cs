namespace Zeus;

/// <summary>
/// Modbus 功能码 0x11 返回的服务器识别信息。
/// </summary>
public sealed class ModbusServerId
{
    private readonly byte[] _additionalData;

    /// <summary>
    /// 创建服务器识别信息。
    /// </summary>
    /// <param name="serverId">服务器 ID。</param>
    /// <param name="runIndicatorStatus">运行指示状态。通常 <c>true</c> 对应响应中的 <c>0xFF</c>。</param>
    /// <param name="additionalData">厂商自定义附加数据。</param>
    public ModbusServerId(byte serverId, bool runIndicatorStatus, IReadOnlyList<byte>? additionalData = null)
    {
        ServerId = serverId;
        RunIndicatorStatus = runIndicatorStatus;
        _additionalData = additionalData?.ToArray() ?? [];
    }

    /// <summary>服务器 ID。</summary>
    public byte ServerId { get; }

    /// <summary>运行指示状态。</summary>
    public bool RunIndicatorStatus { get; }

    /// <summary>厂商自定义附加数据。</summary>
    public IReadOnlyList<byte> AdditionalData => _additionalData;
}
