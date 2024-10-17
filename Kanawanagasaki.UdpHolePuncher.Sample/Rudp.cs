namespace Kanawanagasaki.UdpHolePuncher.Sample;

using Kanawanagasaki.RUDP;
using Kanawanagasaki.UdpHolePuncher.Client;
using System.Diagnostics;
using System.Net;
using System.Text;

public class Rudp
{
    public static async Task Main(string[] args)
    {
        await Task.Delay(3000);

        await using var hp1 = new HolePuncherClient(new(IPAddress.Loopback, 9999), "rudp", _ => true) { IsQuerable = true };
        await hp1.Start(TimeSpan.FromSeconds(2), default);
        while (hp1.PunchResult is null)
            await Task.Delay(100);

        await using var hp2 = new HolePuncherClient(new(IPAddress.Loopback, 9999), "rudp", _ => true) { IsQuerable = true };
        await hp2.Start(TimeSpan.FromSeconds(2), default);
        while (hp2.PunchResult is null)
            await Task.Delay(100);

        await hp1.Connect(hp2.PunchResult, default);

        while (!hp1.ConnectedClients.Any())
            await Task.Delay(100);

        while (!hp2.ConnectedClients.Any())
            await Task.Delay(100);

        await using var rudp1 = new RudpClient(hp1, hp2.PunchResult)
        {
            DebugDropPackets = false,
            DebugDropPacketsChance = 0.1d
        };
        rudp1.OnDatagram += (c, x) => Console.Write(Encoding.UTF8.GetString(x.Span));

        await using var rudp2 = new RudpClient(hp2, hp1.PunchResult)
        {
            DebugDropPackets = false,
            DebugDropPacketsChance = 0.1d
        };
        rudp2.OnDatagram += (c, x) => Console.Write(Encoding.UTF8.GetString(x.Span));

        Console.WriteLine("Sending unreliable datagrams from rudp1 to rudp2");
        for (int i = 0; i < 100; i++)
            await rudp1.SendDatagramAsync(Encoding.UTF8.GetBytes($"{i} "), default);
        await Task.Delay(500);
        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine("Sending unreliable datagrams from rudp2 to rudp1");
        for (int i = 0; i < 100; i++)
            await rudp2.SendDatagramAsync(Encoding.UTF8.GetBytes($"{i} "), default);
        await Task.Delay(500);
        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine("Sending reliable datagrams from rudp1 to rudp2");
        for (int i = 0; i < 100; i++)
            await rudp1.SendReliableDatagramAsync(Encoding.UTF8.GetBytes($"{i} "), default);
        await Task.Delay(500);
        while (rudp2.HasMissingReliableDatagrams)
            await Task.Delay(500);
        Console.WriteLine();
        Console.WriteLine();

        Console.WriteLine("Sending reliable datagrams from rudp2 to rudp1");
        for (int i = 0; i < 100; i++)
            await rudp2.SendReliableDatagramAsync(Encoding.UTF8.GetBytes($"{i} "), default);
        await Task.Delay(500);
        while (rudp1.HasMissingReliableDatagrams)
            await Task.Delay(100);
        Console.WriteLine();
        Console.WriteLine();

        var arr = Enumerable.Range(0, 1_000_000).Select(x => (byte)(x % 256)).ToArray();

        for (int i = 0; i < 5; i++)
        {
            Console.WriteLine($"Sending {arr.Length} bytes stream from rudp1 to rudp2");
            await rudp1.WriteAsync(arr, default);

            Console.WriteLine($"Reading {arr.Length} bytes stream");
            var startReading = Stopwatch.GetTimestamp();
            var buffer = new byte[arr.Length * 2].AsMemory();
            int readTotal = 0;
            while (readTotal < arr.Length)
                readTotal += await rudp2.ReadAsync(buffer, default);
            var elapsed = Stopwatch.GetElapsedTime(startReading);
            Console.WriteLine($"Read in {elapsed.TotalSeconds:0.00} seconds ({arr.Length / elapsed.TotalSeconds:0.00} Bps)");
            Console.WriteLine();
        }

        Console.WriteLine("---");

        for (int i = 0; i < 5; i++)
        {
            Console.WriteLine($"Sending {arr.Length} bytes stream from rudp2 to rudp1");
            await rudp2.WriteAsync(arr, default);

            Console.WriteLine($"Reading {arr.Length} bytes stream");
            var startReading = Stopwatch.GetTimestamp();
            var buffer = new byte[arr.Length / 64].AsMemory();
            var readTotal = 0;
            while (readTotal < arr.Length)
                readTotal += await rudp1.ReadAsync(buffer, default);
            var elapsed = Stopwatch.GetElapsedTime(startReading);
            Console.WriteLine($"Read in {elapsed.TotalSeconds:0.00} seconds ({arr.Length / elapsed.TotalSeconds:0.00} Bps)");
            Console.WriteLine();
        }
    }
}
