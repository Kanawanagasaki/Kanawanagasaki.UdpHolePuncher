using Kanawanagasaki.UdpHolePuncher.Client;
using Kanawanagasaki.UdpHolePuncher.Contracts;
using System.Net;
using System.Text;

{
    Console.WriteLine("Waiting 3 seconds...");
    await Task.Delay(3000);

    HolePuncherClient client2;
    HolePuncherClient client3;
    QueryRes? queryRes;
    await using (var client1 = new HolePuncherClient(new(IPAddress.Loopback, 9999))
    {
        Tags = ["A", "B", "C"],
        Name = "ABC"
    })
    {
        client1.Start();
        Console.WriteLine("Client 1 started");
        await Task.Delay(1000);

        client2 = new HolePuncherClient(new(IPAddress.Loopback, 9999))
        {
            Tags = ["Foo", "Bar", "Baz"],
            Name = "Hello, world!",
            Extra = Encoding.UTF8.GetBytes("FizzBazz")
        };
        client2.Start();
        Console.WriteLine("Client 2 started");
        await Task.Delay(1000);

        client3 = new HolePuncherClient(new(IPAddress.Loopback, 9999));
        client3.Start();
        Console.WriteLine("Client 3 started");
        await Task.Delay(1000);

        queryRes = await client1.Query([], TimeSpan.FromSeconds(10));
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

        await client1.Stop();
        Console.WriteLine("Client 1 stopped");
        await Task.Delay(601000);

        Console.WriteLine("Quering client 2");
        queryRes = await client2.Query([], TimeSpan.FromSeconds(10));
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

        client1.Start();
        Console.WriteLine("Client 1 started");
        queryRes = await client1.Query([], TimeSpan.FromSeconds(10));
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

    await Task.Delay(601000);

    Console.WriteLine("Quering client 3");
    queryRes = await client3.Query([], TimeSpan.FromSeconds(10));
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
