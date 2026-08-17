namespace Zeus;

/// <summary>
/// EtherNet/IP 虚拟 PLC 的内存映像。用于测试、示例与无硬件联调。
/// </summary>
public sealed class EtherNetIpSlaveMemory
{
    private readonly Dictionary<string, EtherNetIpTagValue> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _attributes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>预置或更新标签值。</summary>
    public void SetTag(string tagName, EtherNetIpDataType dataType, object value)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new ZeusException("EtherNet/IP 标签名不能为空。");
        }

        _tags[tagName.Trim()] = new EtherNetIpTagValue(dataType, value);
    }

    /// <summary>尝试读取标签值。</summary>
    public bool TryGetTag(string tagName, out EtherNetIpTagValue value)
    {
        if (_tags.TryGetValue(tagName.Trim(), out var found))
        {
            value = found;
            return true;
        }

        value = null!;
        return false;
    }

    /// <summary>读取标签值；不存在时抛出。</summary>
    public EtherNetIpTagValue GetTag(string tagName)
    {
        if (_tags.TryGetValue(tagName.Trim(), out var value))
        {
            return value;
        }

        throw new ZeusException($"EtherNet/IP 虚拟 PLC 中不存在标签 {tagName}。");
    }

    /// <summary>预置或更新 CIP 对象属性。</summary>
    public void SetAttribute(ushort classId, uint instanceId, ushort attributeId, ReadOnlySpan<byte> value)
        => _attributes[AttributeKey(classId, instanceId, attributeId)] = value.ToArray();

    /// <summary>尝试读取 CIP 对象属性。</summary>
    public bool TryGetAttribute(ushort classId, uint instanceId, ushort attributeId, out byte[] value)
        => _attributes.TryGetValue(AttributeKey(classId, instanceId, attributeId), out value!);

    private static string AttributeKey(ushort classId, uint instanceId, ushort attributeId)
        => classId + "/" + instanceId + "/" + attributeId;
}

/// <summary>
/// EtherNet/IP 虚拟 PLC 中的单个标签值。
/// </summary>
public sealed class EtherNetIpTagValue
{
    internal EtherNetIpTagValue(EtherNetIpDataType dataType, object value)
    {
        DataType = dataType;
        Value = value;
    }

    /// <summary>CIP 数据类型。</summary>
    public EtherNetIpDataType DataType { get; }

    /// <summary>当前值。</summary>
    public object Value { get; }
}
