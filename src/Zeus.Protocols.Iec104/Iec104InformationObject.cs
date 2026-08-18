namespace Zeus;

/// <summary>
/// IEC104 信息对象值。地址为 3 字节 IOA，值按 <see cref="DataType"/> 解释。
/// </summary>
/// <param name="Address">信息对象地址 IOA，范围 0 到 16777215。</param>
/// <param name="DataType">信息对象类型。</param>
/// <param name="Value">已解码值：单点为 <see cref="bool"/>，测量值为数值。</param>
/// <param name="Quality">质量描述字节。</param>
/// <param name="Cause">传送原因。</param>
public readonly record struct Iec104InformationObject(
    int Address,
    Iec104DataType DataType,
    object Value,
    byte Quality = 0,
    Iec104CauseOfTransmission Cause = Iec104CauseOfTransmission.InterrogatedByStation);
