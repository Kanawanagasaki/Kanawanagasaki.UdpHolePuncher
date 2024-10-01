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

var clients = new Dictionary<string, List<RemoteClient>>();

Console.WriteLine("Listening on " + localEndpoint);

while (true)
{
    try
    {
        var receiveRes = await udpClient.ReceiveAsync();
        var bytes = receiveRes.Buffer;
        var remoteEndpoint = receiveRes.RemoteEndPoint;

        if (0x7FFF < bytes.Length || bytes.Length < 6 || bytes[0] != 'K' || bytes[1] != 'U' || bytes[2] != 'H' || bytes[3] != 'P')
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($"Received a datagram from {remoteEndpoint} that does not conform to the protocol ({bytes.Length} len{(4 < bytes.Length ? $", {(char)bytes[0]}{(char)bytes[1]}{(char)bytes[2]}{(char)bytes[3]}" : "")})");
            Console.ResetColor();
            continue;
        }

        var now = DateTime.UtcNow;
        foreach (var (_, clientsList) in clients)
        {
            for (int i = 0; i < clientsList.Count; i++)
            {
                if (TimeSpan.FromMinutes(10) < now - clientsList[i].LastPunch)
                {
                    Console.WriteLine("Client removed:");
                    Console.WriteLine(clientsList[i]);

                    clientsList.RemoveAt(i);
                    i--;
                }
            }
        }

        foreach (var project in clients.Keys)
            if (clients[project].Count == 0)
                clients.Remove(project);

        var op = (EOperation)((bytes[4] << 8) | bytes[5]);
        switch (op)
        {
            case EOperation.Punch:
                {
                    var punch = Serializer.Deserialize<PunchOp>(bytes.AsSpan(6));
                    if (punch is null)
                        break;
                    RemoteClient? client = null;
                    var clientsList = clients.GetValueOrDefault(punch.Token ?? string.Empty);
                    if (clientsList is not null)
                        client = clientsList.FirstOrDefault(x => x.EndPoint.Equals(remoteEndpoint));

                    if (client is null)
                    {
                        client = new()
                        {
                            IpBytes = remoteEndpoint.Address.GetAddressBytes(),
                            Port = remoteEndpoint.Port,
                            Token = punch.Token,
                            Name = punch.Name,
                            Extra = punch.Extra,
                            Tags = punch.Tags,
                            LastPunch = now,
                        };
                        if (clientsList is null)
                            clients[punch.Token ?? string.Empty] = clientsList = new();

                        clientsList.Add(client);
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
                    var query = Serializer.Deserialize<QueryOp>(bytes.AsSpan(6));
                    if (query is null)
                        break;
                    var clientsList = clients.GetValueOrDefault(query.Token ?? string.Empty);
                    var queryClients = clientsList is null ? [] : query.Tags is null ? clientsList.ToArray() : clientsList.Where(c => query.Tags.All(t => c.Tags is not null && c.Tags.Contains(t)));
                    var opNum = (ushort)EOperation.QueryRes;
                    using var memory = new MemoryStream();
                    memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
                    memory.WriteByte((byte)(opNum >> 8));
                    memory.WriteByte((byte)(opNum & 0xFF));
                    Serializer.Serialize(memory, new QueryRes { Clients = queryClients.ToArray() });

                    await udpClient.SendAsync(memory.ToArray(), remoteEndpoint);
                    break;
                }
            case EOperation.Connect:
                {
                    var connect = Serializer.Deserialize<ConnectOp>(bytes.AsSpan(6));
                    if (connect is null)
                        break;

                    var clientsList = clients.GetValueOrDefault(connect.Token ?? string.Empty);
                    if (clientsList is null)
                        break;

                    var remoteClient = clientsList.FirstOrDefault(x => x.EndPoint.Equals(connect.EndPoint));
                    if (remoteClient is null)
                        break;

                    var client = clientsList.FirstOrDefault(x => x.EndPoint.Equals(remoteEndpoint));
                    if (client is null)
                        break;

                    var opNum = (ushort)EOperation.Connect;
                    using var memory = new MemoryStream();
                    memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
                    memory.WriteByte((byte)(opNum >> 8));
                    memory.WriteByte((byte)(opNum & 0xFF));
                    Serializer.Serialize(memory, client);

                    await udpClient.SendAsync(memory.ToArray(), remoteClient.EndPoint);
                    Console.WriteLine($"Connecting {remoteEndpoint} and {remoteClient.EndPoint}");
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
