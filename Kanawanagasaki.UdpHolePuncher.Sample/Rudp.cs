namespace Kanawanagasaki.UdpHolePuncher.Sample;

using Kanawanagasaki.RUDP;
using Kanawanagasaki.UdpHolePuncher.Client;
using System.Net;
using System.Text;

public class Rudp
{
    public static async Task Main(string[] args)
    {
        using var hp1 = new HolePuncherClient(new(IPAddress.Loopback, 9999), "rudp", _ => true);
        await hp1.Start(default);
        while (hp1.PunchResult is null)
            await Task.Delay(100);

        using var hp2 = new HolePuncherClient(new(IPAddress.Loopback, 9999), "rudp", _ => true);
        await hp2.Start(default);
        while (hp2.PunchResult is null)
            await Task.Delay(100);

        await hp1.Connect(hp2.PunchResult, default);

        while (!hp1.ConnectedClients.Any())
            await Task.Delay(100);

        while (!hp2.ConnectedClients.Any())
            await Task.Delay(100);

        using var rudp1 = new RudpClient(hp1, hp2.PunchResult)
        {
            DebugDropPackets = true,
            DebugDropPacketsChance = 0.5d
        };
        rudp1.OnDatagram += (x) => Console.Write(Encoding.UTF8.GetString(x.Span));

        using var rudp2 = new RudpClient(hp2, hp1.PunchResult)
        {
            DebugDropPackets = true,
            DebugDropPacketsChance = 0.5d
        };
        rudp2.OnDatagram += (x) => Console.Write(Encoding.UTF8.GetString(x.Span));

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

        var arr = Encoding.ASCII.GetBytes(string.Join(" ", Enumerable.Range(0, 1024).Select((x, i) => i)));
        Console.WriteLine($"Sending {arr.Length} bytes stream from rudp1 to rudp2");
        await rudp1.WriteAsync(arr, default);

        Console.WriteLine($"Reading {arr.Length} bytes stream");
        var buffer = new byte[arr.Length * 2];
        int readTotal = 0;
        while (readTotal < arr.Length)
        {
            var read = await rudp2.ReadAsync(buffer, default);
            Console.WriteLine($"Read {read} bytes");
            Console.WriteLine(Encoding.ASCII.GetString(buffer, 0, read));
            readTotal += read;
        }
        Console.WriteLine();

        Console.WriteLine($"Sending {arr.Length} bytes stream from rudp2 to rudp1");
        await rudp2.WriteAsync(arr, default);

        Console.WriteLine($"Reading {arr.Length} bytes stream");
        buffer = new byte[arr.Length / 64];
        readTotal = 0;
        while (readTotal < arr.Length)
        {
            var read = await rudp1.ReadAsync(buffer, default);
            Console.WriteLine($"Read {read} bytes");
            Console.WriteLine(Encoding.ASCII.GetString(buffer, 0, read));
            readTotal += read;
        }
        Console.WriteLine();
    }
}
