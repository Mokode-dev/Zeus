using Zeus;

var options = new Iec104Options { CommonAddress = 7 };
var memory = new Iec104StationMemory();
memory.SetSinglePoint(1, true);
memory.SetScaled(100, 253);
memory.SetShortFloat(200, 25.3);

await using var host = ZeusHost.Create(builder =>
{
    builder.AddAcquisition(TimeSpan.FromMilliseconds(500));
    builder.AddVirtualChannel("iec-link", new Iec104SlaveResponder(options, memory));
    builder.AddIec104(
        "station",
        "iec-link",
        options,
        points: map => map
            .SinglePoint("running", 1)
            .Scaled("temperature", 100, scale: 0.1)
            .ShortFloat("pressure", 200));
});

await host.StartAsync();

var station = host.Devices.Get<Iec104Device>("station");
var values = await station.InterrogateAsync();

Console.WriteLine("IEC 60870-5-104 station common address 7");
foreach (var value in values)
{
    Console.WriteLine($"IOA {value.Address}: {value.DataType} = {value.Value}");
}

await host.StopAsync();
