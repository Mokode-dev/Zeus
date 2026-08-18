namespace Zeus;

/// <summary>
/// IEC 60870-5-104 当前内置支持的信息对象类型。
/// </summary>
public enum Iec104DataType
{
    /// <summary>单点信息 <c>M_SP_NA_1</c> / 单点命令 <c>C_SC_NA_1</c>。</summary>
    SinglePoint = 1,

    /// <summary>归一化测量值 <c>M_ME_NA_1</c> / 归一化设点命令 <c>C_SE_NA_1</c>。</summary>
    Normalized = 9,

    /// <summary>标度化测量值 <c>M_ME_NB_1</c> / 标度化设点命令 <c>C_SE_NB_1</c>。</summary>
    Scaled = 11,

    /// <summary>短浮点测量值 <c>M_ME_NC_1</c> / 短浮点设点命令 <c>C_SE_NC_1</c>。</summary>
    ShortFloat = 13
}
