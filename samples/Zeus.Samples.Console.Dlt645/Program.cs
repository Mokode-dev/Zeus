using Zeus;

const string meterAddress = "000000000001";

var memory = new Dlt645SlaveMemory();
memory.SetBcd(0x00000000, 1234.56, byteLength: 4, scale: 0.01);
memory.SetBcd(0x02010100, 220.5, byteLength: 2, scale: 0.1);

await using var host = ZeusHost.Create(builder =>
{
    builder.AddAcquisition(TimeSpan.FromMilliseconds(500));
    builder.AddVirtualChannel("meter-link", new Dlt645SlaveResponder(meterAddress, memory));
    builder.AddDlt645(
        "meter",
        "meter-link",
        new Dlt645Options { MeterAddress = meterAddress, WakeUpPreambleCount = 0 },
        points: map => map
            .Bcd("energy", 0x00000000, dataLength: 4, scale: 0.01)
            .Bcd("voltageA", 0x02010100, dataLength: 2, scale: 0.1));
});

await host.StartAsync();

var meter = host.Devices.Get<Dlt645Device>("meter");
var energy = await meter.ReadBcdAsync(0x00000000, byteLength: 4, scale: 0.01);
var voltageA = await meter.ReadBcdAsync(0x02010100, byteLength: 2, scale: 0.1);

Console.WriteLine($"DL/T 645 meter {meterAddress}");
Console.WriteLine($"Energy: {energy:0.00} kWh");
Console.WriteLine($"Voltage A: {voltageA:0.0} V");

await host.StopAsync();
