namespace Kanawanagasaki.UdpHolePuncher.Sample;

using Kanawanagasaki.UdpHolePuncher.Client;
using System.Net;
using System.Reflection;
using System.Text;

public class HolePuncherSuddenDisconnect
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Staring suddendisconnecttest...");
        var clientA = new HolePuncherClient(new(IPAddress.Loopback, 9999), "suddendisconnecttest", _ => true)
        {
            Name = "AAAAA",
            Tags = ["aaaaa"],
            InactiveClientsDisconnectTimeSpan = TimeSpan.FromSeconds(10)
        };
        clientA.OnRemoteClientConnected += (remoteClient) => Console.WriteLine($"{remoteClient.Name} connected to AAAAA");
        clientA.OnData += (remoteClient, data) => Console.WriteLine($"{remoteClient.Name}: {Encoding.UTF8.GetString(data)}");
        clientA.OnRemoteClientDisconnected += (remoteClient) => Console.WriteLine($"{remoteClient.Name} disconnected from AAAAA");

        await clientA.Start(default);

        while (clientA.PunchResult is null)
            await Task.Delay(100);

        var clientB = new HolePuncherClient(new(IPAddress.Loopback, 9999), "suddendisconnecttest", _ => true)
        {
            Name = "BBBBB",
            InactiveClientsDisconnectTimeSpan = TimeSpan.FromSeconds(10)
        };
        clientB.OnRemoteClientConnected += (remoteClient) => Console.WriteLine($"{remoteClient.Name} connected to BBBBB");
        clientB.OnData += (remoteClient, data) => Console.WriteLine($"{remoteClient.Name}: {Encoding.UTF8.GetString(data)}");
        clientB.OnRemoteClientDisconnected += (remoteClient) => Console.WriteLine($"{remoteClient.Name} disconnected from BBBBB");

        await clientB.Start(default);

        while (clientB.PunchResult is null)
            await Task.Delay(100);

        var queryRes = await clientB.Query(["aaaaa"], TimeSpan.FromSeconds(1), default);
        if (queryRes?.Clients is null || queryRes.Clients.Length == 0)
            Console.WriteLine("Query result or clients was null or length was 0");
        else
        {
            await clientB.Connect(queryRes.Clients[0], default);
            while (!clientB.ConnectedClients.Any())
                await Task.Delay(100);
            await clientB.SendTo(queryRes.Clients[0], Encoding.UTF8.GetBytes("I showed you my speedrun, please respond"), default);
        }

    ((CancellationTokenSource)clientA.GetType().GetField("_tickCts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(clientA)!).Cancel();
        ((CancellationTokenSource)clientA.GetType().GetField("_receiveCts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(clientA)!).Cancel();

        var wait = clientB.InactiveClientsDisconnectTimeSpan + clientB.TickPeriod + TimeSpan.FromSeconds(1);
        Console.WriteLine($"Waiting {wait.Seconds:0.##} seconds...");
        await Task.Delay(wait);
        Console.WriteLine($"After {wait} BBBBB client has {clientB.ConnectedClients.Count()} connected clients");
    }
}
