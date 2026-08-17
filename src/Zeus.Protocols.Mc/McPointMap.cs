namespace Zeus;

/// <summary>
/// 声明一台 Mitsubishi MC 设备上要周期采集的点。连续软元件地址会在采集时自动合并为一次读取。
/// D/W/R/ZR 字软元件与 M/Y 位软元件可再调用 <see cref="Writable"/>，以便按点名写回。
/// </summary>
public sealed class McPointMap
{
    private const int MaxAddress = 0xFFFFFF;

    private readonly List<McPointSpec> _points = [];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已声明的点，登记顺序。</summary>
    internal IReadOnlyList<McPointSpec> Points => _points;

    /// <summary>声明一个 D 数据寄存器点，值为原始 <see cref="ushort"/>。</summary>
    public McPointMap DataRegister(string name, int address)
        => Word(name, McDeviceCode.DataRegister, address);

    /// <summary>声明一个带报警限的 D 数据寄存器点，值为原始 <see cref="ushort"/>。</summary>
    public McPointMap DataRegister(string name, int address, PointAlarmLimits alarmLimits)
        => Word(name, McDeviceCode.DataRegister, address, alarmLimits);

    /// <summary>声明一个带线性换算的 D 数据寄存器点。</summary>
    public McPointMap DataRegister(string name, int address, double scale)
        => Word(name, McDeviceCode.DataRegister, address, scale);

    /// <summary>声明一个带线性换算和报警限的 D 数据寄存器点。</summary>
    public McPointMap DataRegister(string name, int address, double scale, PointAlarmLimits alarmLimits)
        => Word(name, McDeviceCode.DataRegister, address, scale, alarmLimits);

    /// <summary>声明一个 W 链接寄存器点，值为原始 <see cref="ushort"/>。</summary>
    public McPointMap LinkRegister(string name, int address)
        => Word(name, McDeviceCode.LinkRegister, address);

    /// <summary>声明一个带报警限的 W 链接寄存器点。</summary>
    public McPointMap LinkRegister(string name, int address, PointAlarmLimits alarmLimits)
        => Word(name, McDeviceCode.LinkRegister, address, alarmLimits);

    /// <summary>声明一个带线性换算的 W 链接寄存器点。</summary>
    public McPointMap LinkRegister(string name, int address, double scale)
        => Word(name, McDeviceCode.LinkRegister, address, scale);

    /// <summary>声明一个带线性换算和报警限的 W 链接寄存器点。</summary>
    public McPointMap LinkRegister(string name, int address, double scale, PointAlarmLimits alarmLimits)
        => Word(name, McDeviceCode.LinkRegister, address, scale, alarmLimits);

    /// <summary>声明一个 R 文件寄存器点，值为原始 <see cref="ushort"/>。</summary>
    public McPointMap FileRegister(string name, int address)
        => Word(name, McDeviceCode.FileRegister, address);

    /// <summary>声明一个带报警限的 R 文件寄存器点。</summary>
    public McPointMap FileRegister(string name, int address, PointAlarmLimits alarmLimits)
        => Word(name, McDeviceCode.FileRegister, address, alarmLimits);

    /// <summary>声明一个带线性换算的 R 文件寄存器点。</summary>
    public McPointMap FileRegister(string name, int address, double scale)
        => Word(name, McDeviceCode.FileRegister, address, scale);

    /// <summary>声明一个带线性换算和报警限的 R 文件寄存器点。</summary>
    public McPointMap FileRegister(string name, int address, double scale, PointAlarmLimits alarmLimits)
        => Word(name, McDeviceCode.FileRegister, address, scale, alarmLimits);

    /// <summary>声明一个 ZR 扩展文件寄存器点，值为原始 <see cref="ushort"/>。1E 帧不支持 ZR。</summary>
    public McPointMap ExtendedFileRegister(string name, int address)
        => Word(name, McDeviceCode.ExtendedFileRegister, address);

    /// <summary>声明一个带报警限的 ZR 扩展文件寄存器点。1E 帧不支持 ZR。</summary>
    public McPointMap ExtendedFileRegister(string name, int address, PointAlarmLimits alarmLimits)
        => Word(name, McDeviceCode.ExtendedFileRegister, address, alarmLimits);

    /// <summary>声明一个带线性换算的 ZR 扩展文件寄存器点。1E 帧不支持 ZR。</summary>
    public McPointMap ExtendedFileRegister(string name, int address, double scale)
        => Word(name, McDeviceCode.ExtendedFileRegister, address, scale);

    /// <summary>声明一个带线性换算和报警限的 ZR 扩展文件寄存器点。1E 帧不支持 ZR。</summary>
    public McPointMap ExtendedFileRegister(string name, int address, double scale, PointAlarmLimits alarmLimits)
        => Word(name, McDeviceCode.ExtendedFileRegister, address, scale, alarmLimits);

    /// <summary>声明一个字软元件点，值为原始 <see cref="ushort"/>。</summary>
    public McPointMap Word(string name, McDeviceCode deviceCode, int address)
        => AddWord(name, deviceCode, address, PointValueKind.UInt16, null, null);

    /// <summary>声明一个带报警限的字软元件点，值为原始 <see cref="ushort"/>。</summary>
    public McPointMap Word(string name, McDeviceCode deviceCode, int address, PointAlarmLimits alarmLimits)
        => AddWord(name, deviceCode, address, PointValueKind.UInt16, null, alarmLimits);

    /// <summary>声明一个带换算函数的字软元件点。</summary>
    public McPointMap Word(string name, McDeviceCode deviceCode, int address, Func<ushort, double> convert)
        => AddWord(name, deviceCode, address, PointValueKind.Double, raw => convert(raw), null);

    /// <summary>声明一个带换算函数和报警限的字软元件点。</summary>
    public McPointMap Word(
        string name,
        McDeviceCode deviceCode,
        int address,
        Func<ushort, double> convert,
        PointAlarmLimits alarmLimits)
        => AddWord(name, deviceCode, address, PointValueKind.Double, raw => convert(raw), alarmLimits);

    /// <summary>声明一个带线性换算的字软元件点。写回时会按同一系数反算。</summary>
    public McPointMap Word(string name, McDeviceCode deviceCode, int address, double scale)
        => AddScaledWord(name, deviceCode, address, scale, null);

    /// <summary>声明一个带线性换算和报警限的字软元件点。</summary>
    public McPointMap Word(string name, McDeviceCode deviceCode, int address, double scale, PointAlarmLimits alarmLimits)
        => AddScaledWord(name, deviceCode, address, scale, alarmLimits);

    /// <summary>声明一个 M 内部继电器点。</summary>
    public McPointMap InternalRelay(string name, int address)
        => Bit(name, McDeviceCode.InternalRelay, address);

    /// <summary>声明一个 X 输入继电器点。输入继电器只读，不能标为可写。</summary>
    public McPointMap InputRelay(string name, int address)
        => Bit(name, McDeviceCode.InputRelay, address);

    /// <summary>声明一个 Y 输出继电器点。</summary>
    public McPointMap OutputRelay(string name, int address)
        => Bit(name, McDeviceCode.OutputRelay, address);

    /// <summary>声明一个位软元件点。</summary>
    public McPointMap Bit(string name, McDeviceCode deviceCode, int address)
    {
        var normalized = Normalize(name);
        EnsureBitDevice(deviceCode, normalized);
        Add(new McPointSpec(normalized, deviceCode, ValidateAddress(address, normalized), PointValueKind.Boolean, null, null));
        return this;
    }

    /// <summary>
    /// 把已经声明的点标为可写，之后可通过 <see cref="IPointTable.WriteAsync"/> 按名称下发。
    /// X 输入继电器不能写；使用自定义换算函数、未提供 <c>scale</c> 的点也无法自动反算。
    /// </summary>
    /// <param name="name">点名。</param>
    public McPointMap Writable(string name)
    {
        var normalized = Normalize(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (!string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (point.DeviceCode == McDeviceCode.InputRelay)
            {
                throw new ZeusException($"点 {normalized} 位于 X 输入继电器，该软元件只读，不能标为可写。");
            }

            if (point.Convert is not null && point.Scale is null)
            {
                throw new ZeusException(
                    $"点 {normalized} 使用了自定义换算函数，无法把工程值反算为字软元件。请改用 Word(\"{normalized}\", deviceCode, address, scale) 再调用 Writable。");
            }

            _points[i] = point.WithWritable(true);
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再标为可写。");
    }

    /// <summary>
    /// 为已经声明的数值点设置或替换报警限。
    /// </summary>
    /// <param name="name">点名。</param>
    /// <param name="low">低报阈值。</param>
    /// <param name="high">高报阈值。</param>
    public McPointMap WithAlarmLimits(string name, double? low = null, double? high = null)
    {
        var normalized = Normalize(name);
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (!string.Equals(point.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (point.Kind == PointValueKind.Boolean)
            {
                throw new ZeusException($"点 {normalized} 是布尔点，不能配置数值报警限。");
            }

            _points[i] = point.WithAlarmLimits(new PointAlarmLimits(low, high));
            return this;
        }

        throw new ZeusException($"找不到点 {normalized}，请先声明该点再配置报警限。");
    }

    private McPointMap AddWord(
        string name,
        McDeviceCode deviceCode,
        int address,
        PointValueKind kind,
        Func<ushort, object>? convert,
        PointAlarmLimits? alarmLimits)
    {
        var normalized = Normalize(name);
        EnsureWordDevice(deviceCode, normalized);
        Add(new McPointSpec(normalized, deviceCode, ValidateAddress(address, normalized), kind, convert, alarmLimits));
        return this;
    }

    private McPointMap AddScaledWord(
        string name,
        McDeviceCode deviceCode,
        int address,
        double scale,
        PointAlarmLimits? alarmLimits)
    {
        var normalized = Normalize(name);
        EnsureWordDevice(deviceCode, normalized);
        if (scale <= 0 || !double.IsFinite(scale))
        {
            throw new ZeusException($"点 {normalized} 的 scale 必须是大于 0 的有限数值。");
        }

        Add(new McPointSpec(
            normalized,
            deviceCode,
            ValidateAddress(address, normalized),
            PointValueKind.Double,
            raw => raw * scale,
            alarmLimits,
            writable: false,
            scale: scale));
        return this;
    }

    private void Add(McPointSpec spec)
    {
        if (!_names.Add(spec.Name))
        {
            throw new ZeusException($"同一台 MC 设备上点名 {spec.Name} 重复。");
        }

        _points.Add(spec);
    }

    private static void EnsureWordDevice(McDeviceCode deviceCode, string pointName)
    {
        if (!McPointSpec.IsWordDevice(deviceCode))
        {
            throw new ZeusException($"点 {pointName} 使用的软元件 {DescribeDeviceCode(deviceCode)} 不是字软元件。");
        }
    }

    private static void EnsureBitDevice(McDeviceCode deviceCode, string pointName)
    {
        if (!McPointSpec.IsBitDevice(deviceCode))
        {
            throw new ZeusException($"点 {pointName} 使用的软元件 {DescribeDeviceCode(deviceCode)} 不是位软元件。");
        }
    }

    private static int ValidateAddress(int address, string pointName)
    {
        if (address is < 0 or > MaxAddress)
        {
            throw new ZeusException($"点 {pointName} 的 MC 地址必须介于 0 与 {MaxAddress} 之间，当前为 {address}。");
        }

        return address;
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ZeusException("MC 点名不能为空。");
        }

        return name.Trim();
    }

    private static string DescribeDeviceCode(McDeviceCode deviceCode)
        => deviceCode switch
        {
            McDeviceCode.InternalRelay => "M 内部继电器",
            McDeviceCode.InputRelay => "X 输入继电器",
            McDeviceCode.OutputRelay => "Y 输出继电器",
            McDeviceCode.DataRegister => "D 数据寄存器",
            McDeviceCode.LinkRegister => "W 链接寄存器",
            McDeviceCode.FileRegister => "R 文件寄存器",
            McDeviceCode.ExtendedFileRegister => "ZR 扩展文件寄存器",
            _ => deviceCode.ToString()
        };
}
