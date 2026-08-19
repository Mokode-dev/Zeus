namespace Zeus;

/// <summary>SNMP PDU 错误状态。</summary>
public enum SnmpErrorStatus
{
    /// <summary>无错误。</summary>
    NoError = 0,

    /// <summary>响应过大。</summary>
    TooBig = 1,

    /// <summary>OID 不存在。</summary>
    NoSuchName = 2,

    /// <summary>值无效。</summary>
    BadValue = 3,

    /// <summary>只读变量。</summary>
    ReadOnly = 4,

    /// <summary>通用错误。</summary>
    GenErr = 5,

    /// <summary>无访问权限。</summary>
    NoAccess = 6,

    /// <summary>值类型错误。</summary>
    WrongType = 7,

    /// <summary>值长度错误。</summary>
    WrongLength = 8,

    /// <summary>编码错误。</summary>
    WrongEncoding = 9,

    /// <summary>值不合法。</summary>
    WrongValue = 10,

    /// <summary>变量不能创建。</summary>
    NoCreation = 11,

    /// <summary>值不一致。</summary>
    InconsistentValue = 12,

    /// <summary>资源不可用。</summary>
    ResourceUnavailable = 13,

    /// <summary>提交失败。</summary>
    CommitFailed = 14,

    /// <summary>回滚失败。</summary>
    UndoFailed = 15,

    /// <summary>授权错误。</summary>
    AuthorizationError = 16,

    /// <summary>不可写。</summary>
    NotWritable = 17,

    /// <summary>名称不一致。</summary>
    InconsistentName = 18
}
