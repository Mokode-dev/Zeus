namespace Zeus;

/// <summary>
/// Modbus 线上封装。PDU 相同，仅帧头、编码与校验不同。
/// </summary>
public enum ModbusTransport
{
    /// <summary>串口 RTU：地址 + PDU + CRC16。</summary>
    Rtu = 0,

    /// <summary>TCP：MBAP 头 + 单元标识 + PDU。</summary>
    Tcp = 1,

    /// <summary>ASCII：冒号起始、十六进制文本、LRC 校验、CRLF 结束。</summary>
    Ascii = 2
}
