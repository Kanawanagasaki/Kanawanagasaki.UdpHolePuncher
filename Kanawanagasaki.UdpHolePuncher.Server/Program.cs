using Kanawanagasaki.UdpHolePuncher.Contracts;
using ProtoBuf;
using System.Net;
using System.Net.Sockets;

ushort port;
var portStr = Environment.GetEnvironmentVariable("KANAWANAGASAKI_UDPHOLEPUNCHER_PORT");
if (portStr is null || !ushort.TryParse(portStr, out port))
    port = 9999;

var localEndpoint = new IPEndPoint(IPAddress.Any, port);

var udpClient = new UdpClient(localEndpoint);

var clients = new List<RemoteClient>();

Console.WriteLine("Listening on " + localEndpoint);

while (true)
{
    try
    {
        var receiveRes = await udpClient.ReceiveAsync();
        var bytes = receiveRes.Buffer;
        var remoteEndpoint = receiveRes.RemoteEndPoint;

        if (bytes.Length < 6)
            continue;
        if (bytes[0] != 'K' || bytes[1] != 'U' || bytes[2] != 'H' || bytes[3] != 'P')
            continue;

        var now = DateTime.UtcNow;
        for (int i = 0; i < clients.Count; i++)
        {
            if (TimeSpan.FromMinutes(10) < now - clients[i].LastPunch)
            {
                Console.WriteLine("Client removed:");
                Console.WriteLine(clients[i]);

                clients.RemoveAt(i);
                i--;
            }
        }

        var op = (EOperation)((bytes[4] << 8) | bytes[5]);
        switch (op)
        {
            case EOperation.Punch:
                {
                    var punch = Serializer.Deserialize<PunchOp>(bytes[6..].AsMemory());
                    if (punch is null)
                        break;
                    var ipBytes = remoteEndpoint.Address.GetAddressBytes();
                    var client = clients.FirstOrDefault(x => Enumerable.SequenceEqual(x.IpBytes ?? [], ipBytes) && x.Port == remoteEndpoint.Port);
                    if (client is null)
                    {
                        client = new()
                        {
                            IpBytes = ipBytes,
                            Port = remoteEndpoint.Port,
                            Name = punch.Name,
                            Extra = punch.Extra,
                            Tags = punch.Tags,
                            LastPunch = now,
                        };
                        clients.Add(client);
                        Console.WriteLine("Client added:");
                        Console.WriteLine(client);
                    }
                    else
                    {
                        client.Name = punch.Name;
                        client.Extra = punch.Extra;
                        client.Tags = punch.Tags;
                        client.LastPunch = now;
                    }

                    var opNum = (ushort)EOperation.PunchRes;
                    using var memory = new MemoryStream();
                    memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
                    memory.WriteByte((byte)(opNum >> 8));
                    memory.WriteByte((byte)(opNum & 0xFF));
                    Serializer.Serialize(memory, client);

                    await udpClient.SendAsync(memory.ToArray(), remoteEndpoint);
                    break;
                }
            case EOperation.Query:
                {
                    var query = Serializer.Deserialize<QueryOp>(bytes[6..].AsMemory());
                    if (query is null)
                        break;
                    var queryClients = query.Tags is null ? clients.ToArray() : clients.Where(c => query.Tags.All(t => c.Tags is not null && c.Tags.Contains(t))).ToArray();
                    var opNum = (ushort)EOperation.QueryRes;
                    using var memory = new MemoryStream();
                    memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
                    memory.WriteByte((byte)(opNum >> 8));
                    memory.WriteByte((byte)(opNum & 0xFF));
                    Serializer.Serialize(memory, new QueryRes { Clients = queryClients });

                    await udpClient.SendAsync(memory.ToArray(), remoteEndpoint);
                    break;
                }
        }
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
        Console.WriteLine(e.StackTrace);
    }
}
