namespace Zeus;

internal readonly record struct Iec104PointSpec(
    string Name,
    int Address,
    Iec104DataType DataType,
    double? Scale,
    PointValueKind Kind,
    PointAlarmLimits? AlarmLimits,
    bool Writable)
{
    public Iec104PointSpec WithWritable(bool writable) => this with { Writable = writable };

    public Iec104PointSpec WithAlarmLimits(PointAlarmLimits? alarmLimits) => this with { AlarmLimits = alarmLimits };
}
