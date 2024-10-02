using Kanawanagasaki.UdpHolePuncher.Client;
using Kanawanagasaki.UdpHolePuncher.Contracts;
using System.Net;
using System.Text;

{
    var ip = IPAddress.Loopback;
    Console.WriteLine($"Hostname resolved to {ip} ip address");

    Console.WriteLine("Waiting 3 seconds...");
    await Task.Delay(3000);

    HolePuncherClient client2;
    HolePuncherClient client3;
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
        await client1.Start();
        Console.WriteLine("Client 1 started");
        await Task.Delay(1000);

        client2 = new HolePuncherClient(new(ip, 9999), "test", _ => true)
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
        await client2.Start();
        Console.WriteLine("Client 2 started");
        await Task.Delay(1000);

        client3 = new HolePuncherClient(new(ip, 9999), "test", _ => true);
        client3.OnRemoteClientConnected += c =>
        {
            Console.WriteLine($"{c.Name}@{c.EndPoint} connected to client 3");
            _ = client3.SendTo(c, Encoding.UTF8.GetBytes("Well hello there"), default);
        };
        client3.OnData += (c, d) => Console.WriteLine($"{c.Name}@{c.EndPoint} sent to client 3:\n{Encoding.UTF8.GetString(d)}");
        client3.OnRemoteClientDisconnected += c => Console.WriteLine($"{c.Name}@{c.EndPoint} disconnected from client 3");
        await client3.Start();
        Console.WriteLine("Client 3 started");
        await Task.Delay(1000);

        while (client1.PunchResult is null)
            await Task.Delay(1000);
        while (client2.PunchResult is null)
            await Task.Delay(1000);
        while (client3.PunchResult is null)
            await Task.Delay(1000);

        Console.WriteLine("All 3 clients punched their way through");

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
                    await Task.Delay(1000);
                await client1.SendTo(toSendTo, Encoding.UTF8.GetBytes("This is THE message"), default);
                Console.WriteLine($"Client 1 sent data to {toSendTo.Name}@{toSendTo.EndPoint}");
            }
        }

        await client1.Stop();
        Console.WriteLine("Client 1 stopped");
        await Task.Delay(5000);

        Console.WriteLine("Quering client 2");
        queryRes = await client2.Query([], TimeSpan.FromSeconds(10), default);
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
        }

        await client1.Start();
        Console.WriteLine("Client 1 started");
        queryRes = await client1.Query([], TimeSpan.FromSeconds(10), default);
        Console.WriteLine("Quering client 1");
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
        }
    }
    Console.WriteLine("Client 1 dispossed");
    await client2.DisposeAsync();
    Console.WriteLine("Client 2 dispossed");

    await Task.Delay(5000);

    Console.WriteLine("Quering client 3");
    queryRes = await client3.Query([], TimeSpan.FromSeconds(10), default);
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
    }
}
Console.WriteLine("Client 3 dispossed");

Console.WriteLine("""
    ----------
    -- DONE --
    ----------
    """);
Console.ReadLine();
