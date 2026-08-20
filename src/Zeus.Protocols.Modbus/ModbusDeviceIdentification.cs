namespace Zeus;

/// <summary>
/// Modbus 读设备识别（功能码 0x2B / MEI 0x0E）的结果。
/// </summary>
public sealed class ModbusDeviceIdentification
{
    /// <summary>
    /// 创建设备识别结果。
    /// </summary>
    /// <param name="deviceIdCode">请求使用的识别类别。</param>
    /// <param name="conformityLevel">从站符合级别。</param>
    /// <param name="moreFollows">是否还有后续对象。</param>
    /// <param name="nextObjectId">后续起始对象 ID。</param>
    /// <param name="objects">对象 ID 到 ASCII/二进制值的映射。</param>
    public ModbusDeviceIdentification(
        byte deviceIdCode,
        byte conformityLevel,
        bool moreFollows,
        byte nextObjectId,
        IReadOnlyDictionary<byte, byte[]> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        DeviceIdCode = deviceIdCode;
        ConformityLevel = conformityLevel;
        MoreFollows = moreFollows;
        NextObjectId = nextObjectId;
        Objects = new Dictionary<byte, byte[]>(objects);
    }

    /// <summary>请求使用的识别类别：1 基本、2 常规、3 扩展、4 单个对象。</summary>
    public byte DeviceIdCode { get; }

    /// <summary>从站声明的符合级别。</summary>
    public byte ConformityLevel { get; }

    /// <summary>响应是否被截断，需要用 <see cref="NextObjectId"/> 继续读。</summary>
    public bool MoreFollows { get; }

    /// <summary>后续读取的起始对象 ID。</summary>
    public byte NextObjectId { get; }

    /// <summary>对象 ID 到原始值。</summary>
    public IReadOnlyDictionary<byte, byte[]> Objects { get; }

    /// <summary>厂商名（对象 0x00），无法解码时为 <c>null</c>。</summary>
    public string? VendorName => GetAscii(0x00);

    /// <summary>产品代码（对象 0x01）。</summary>
    public string? ProductCode => GetAscii(0x01);

    /// <summary>主次版本（对象 0x02）。</summary>
    public string? MajorMinorRevision => GetAscii(0x02);

    private string? GetAscii(byte objectId)
    {
        if (!Objects.TryGetValue(objectId, out var value) || value.Length == 0)
        {
            return null;
        }

        return System.Text.Encoding.ASCII.GetString(value);
    }
}

/// <summary>
/// 一次文件记录读写的结果。
/// </summary>
/// <param name="FileNumber">文件号。</param>
/// <param name="RecordNumber">记录号。</param>
/// <param name="Values">记录中的寄存器值。</param>
public readonly record struct ModbusFileRecord(ushort FileNumber, ushort RecordNumber, IReadOnlyList<ushort> Values);
