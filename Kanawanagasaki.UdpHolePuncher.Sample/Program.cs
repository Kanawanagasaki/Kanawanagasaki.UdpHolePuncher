using Kanawanagasaki.UdpHolePuncher.Client;
using Kanawanagasaki.UdpHolePuncher.Contracts;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;

HolePuncherClient client3;
{
    var ip = IPAddress.Loopback;
    Console.WriteLine($"Hostname resolved to {ip} ip address");

    Console.WriteLine("Waiting 3 seconds...");
    await Task.Delay(3000);

    QueryRes? queryRes;
    await using (var client1 = new HolePuncherClient(new(ip, 9999), "test", _ => true)
    {
        Tags = ["A", "B", "C"],
        Name = "ABC"
    })
    {
        client1.OnRemoteClientConnected += c => Console.WriteLine($"{c.Name}@{c.EndPoint} connected to client 1");
        client1.OnData += (c, d) =>
        {
            Console.WriteLine($"{c.Name}@{c.EndPoint} sent to client 1:\n{Encoding.UTF8.GetString(d)}");
            _ = client1.SendTo(c, d.Reverse().ToArray(), default);
        };
        client1.OnRemoteClientDisconnected += c => Console.WriteLine($"{c.Name}@{c.EndPoint} disconnected from client 1");
        await client1.Start(default);
        Console.WriteLine("Client 1 started");
        await Task.Delay(100);

        await using var client2 = new HolePuncherClient(new(ip, 9999), "test", _ => true)
        {
            Tags = ["Foo", "Bar", "Baz"],
            Name = "Hello, world!",
            Extra = Encoding.UTF8.GetBytes("FizzBazz")
        };
        client2.OnRemoteClientConnected += c =>
        {
            Console.WriteLine($"{c.Name}@{c.EndPoint} connected to client 2");
            _ = client2.SendTo(c, Encoding.UTF8.GetBytes("Ara ara, henlo"), default);
        };
        client2.OnData += (c, d) => Console.WriteLine($"{c.Name}@{c.EndPoint} sent to client 2:\n{Encoding.UTF8.GetString(d)}");
        client2.OnRemoteClientDisconnected += c => Console.WriteLine($"{c.Name}@{c.EndPoint} disconnected from client 2");
        await client2.Start(default);
        Console.WriteLine("Client 2 started");
        await Task.Delay(100);

        client3 = new HolePuncherClient(new(ip, 9999), "test", _ => true);
        client3.OnRemoteClientConnected += c =>
        {
            Console.WriteLine($"{c.Name}@{c.EndPoint} connected to client 3");
            _ = client3.SendTo(c, Encoding.UTF8.GetBytes("Well hello there"), default);
        };
        client3.OnData += (c, d) => Console.WriteLine($"{c.Name}@{c.EndPoint} sent to client 3:\n{Encoding.UTF8.GetString(d)}");
        client3.OnRemoteClientDisconnected += c => Console.WriteLine($"{c.Name}@{c.EndPoint} disconnected from client 3");
        await client3.Start(default);
        Console.WriteLine("Client 3 started");

        while (client1.PunchResult is null)
            await Task.Delay(100);
        while (client2.PunchResult is null)
            await Task.Delay(100);
        while (client3.PunchResult is null)
            await Task.Delay(100);

        Console.WriteLine("All 3 clients punched their way through");
        Console.WriteLine($"Client 1 is {client1.PunchResult.Name}@{client1.PunchResult.EndPoint}");
        Console.WriteLine($"Client 2 is {client2.PunchResult.Name}@{client2.PunchResult.EndPoint}");
        Console.WriteLine($"Client 3 is {client3.PunchResult.Name}@{client3.PunchResult.EndPoint}");

        queryRes = await client1.Query([], TimeSpan.FromSeconds(10), default);
        if (queryRes?.Clients is null)
            Console.WriteLine("Query Res was null");
        else
        {
            foreach (var client in queryRes.Clients)
            {
                Console.WriteLine(client);
                if (client.Extra is not null)
                    Console.WriteLine("Extra: " + Encoding.UTF8.GetString(client.Extra));
            }

            var toSendTo = queryRes.Clients.FirstOrDefault(x => !x.Equals(client1.PunchResult));
            if (toSendTo is not null)
            {
                await client1.Connect(toSendTo, default);
                Console.WriteLine($"Client 1 is connecting to {toSendTo.Name}@{toSendTo.EndPoint}");
                while (!client1.ConnectedClients.Any(x => x.Equals(toSendTo)))
                    await Task.Delay(100);

                await client1.SendTo(toSendTo, Encoding.UTF8.GetBytes("This is THE message"), default);
                Console.WriteLine($"Client 1 sent data to {toSendTo.Name}@{toSendTo.EndPoint}");
            }
        }

        await client1.Stop(default);
        Console.WriteLine("Client 1 stopped");

        Console.WriteLine("Quering client 2");
        queryRes = await client2.Query([], TimeSpan.FromSeconds(10), default);
        if (queryRes is null)
            Console.WriteLine("Query Res was null");
        else if (queryRes.Clients is null)
            Console.WriteLine("Query Res Clients was null");
        else
        {
            foreach (var client in queryRes.Clients)
            {
                Console.WriteLine(client);
                if (client.Extra is not null)
                    Console.WriteLine("Extra: " + Encoding.UTF8.GetString(client.Extra));
            }
        }

        await client1.Start(default);
        Console.WriteLine("Client 1 started");
        while (client1.PunchResult is null)
            await Task.Delay(100);
        Console.WriteLine("Quering client 1...");
        queryRes = await client1.Query([], TimeSpan.FromSeconds(5), default);
        if (queryRes is null)
            Console.WriteLine("Query Res was null");
        else if (queryRes.Clients is null)
            Console.WriteLine("Query Res Clients was null");
        else
        {
            foreach (var client in queryRes.Clients)
            {
                Console.WriteLine(client);
                if (client.Extra is not null)
                    Console.WriteLine("Extra: " + Encoding.UTF8.GetString(client.Extra));
            }
        }
        Console.WriteLine("Quering client 1 again...");
        queryRes = await client1.Query([], TimeSpan.FromSeconds(5), default);
        if (queryRes is null)
            Console.WriteLine("Query Res was null");
        else if (queryRes.Clients is null)
            Console.WriteLine("Query Res Clients was null");
        else
        {
            foreach (var client in queryRes.Clients)
            {
                Console.WriteLine(client);
                if (client.Extra is not null)
                    Console.WriteLine("Extra: " + Encoding.UTF8.GetString(client.Extra));
            }
        }
    }
    Console.WriteLine("Client 1 dispossed");
    Console.WriteLine("Client 2 dispossed");

    Console.WriteLine("Quering client 3");
    queryRes = await client3.Query(null, TimeSpan.FromSeconds(10), default);
    if (queryRes is null)
        Console.WriteLine("Query Res was null");
    else if (queryRes.Clients is null)
        Console.WriteLine("Query Res Clients was null");
    else
    {
        foreach (var client in queryRes.Clients)
        {
            Console.WriteLine(client);
            if (client.Extra is not null)
                Console.WriteLine("Extra: " + Encoding.UTF8.GetString(client.Extra));
        }
    }
}
await client3.DisposeAsync();
Console.WriteLine("Client 3 dispossed");

Console.WriteLine();

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

Console.WriteLine("""
    ----------
    -- DONE --
    ----------
    """);
Console.ReadLine();
