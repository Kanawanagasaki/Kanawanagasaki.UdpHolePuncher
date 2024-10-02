using Kanawanagasaki.UdpHolePuncher.Contracts;
using Kanawanagasaki.UdpHolePuncher.Server;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using ProtoBuf;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static System.Runtime.InteropServices.JavaScript.JSType;

var keyPairGenerator = new RsaKeyPairGenerator();
keyPairGenerator.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
var keyPair = keyPairGenerator.GenerateKeyPair();
var publicKeyBytes = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public).GetEncoded();
var decryptEngine = new Pkcs1Encoding(new RsaEngine());
decryptEngine.Init(false, keyPair.Private);

ushort port;
var portStr = Environment.GetEnvironmentVariable("KANAWANAGASAKI_UDPHOLEPUNCHER_PORT");
if (portStr is null || !ushort.TryParse(portStr, out port))
    port = 9999;
var localEndpoint = new IPEndPoint(IPAddress.Any, port);
var udpClient = new UdpClient(localEndpoint);
Console.WriteLine("Listening on " + localEndpoint);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await Task.WhenAll(Receive(cts.Token), ClearClients(cts.Token));

async Task Receive(CancellationToken ct)
{
    while (!cts.IsCancellationRequested)
    {
        try
        {
            var receiveRes = await udpClient.ReceiveAsync(cts.Token);
            if (receiveRes.Buffer.Length < 1 || 1024 < receiveRes.Buffer.Length)
                continue;

            var memoryBytes = receiveRes.Buffer.AsMemory();
            var remoteEndpoint = receiveRes.RemoteEndPoint;
            var client = Clients.GetClientByEndpoint(remoteEndpoint);
            Clients.UpdateClientTime(client);

            Debug.WriteLine($"SERVER | Received datagram of size {receiveRes.Buffer.Length} bytes, of type {(EPacketType)receiveRes.Buffer[0]} from {remoteEndpoint}");

            switch ((EPacketType)receiveRes.Buffer[0])
            {
                case EPacketType.RSAPublicKey:
                    await SendPublicRSAKey(remoteEndpoint, ct);
                    break;
                case EPacketType.RSAEncryptedAESKey:
                    await DecryptAndSaveAesKey(client, memoryBytes[1..].ToArray(), ct);
                    break;
                case EPacketType.AESEncryptedData:
                    if (TryDecrypt(client, memoryBytes[1..], out var decrypted))
                        await ProcessPacket(client, decrypted, ct);
                    break;
                case EPacketType.Ping:
                    await SendPacketNoData(remoteEndpoint, EPacketType.Pong, ct);
                    break;
                case EPacketType.Disconnect:
                    Clients.RemoveClient(remoteEndpoint);
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
        }
    }
}

async Task ClearClients(CancellationToken ct)
{
    var lastGroupClear = Stopwatch.GetTimestamp();

    while (!cts.IsCancellationRequested)
    {
        try
        {
            Clients.ClearInactiveClients();

            if (TimeSpan.FromMinutes(15) < Stopwatch.GetElapsedTime(lastGroupClear))
            {
                Clients.ClearEmptyGroups();
                lastGroupClear = Stopwatch.GetTimestamp();
            }

            await Task.Delay(10_000, ct);
        }
        catch (OperationCanceledException) { }
        catch { }
    }
}

async Task SendPacket(IPEndPoint endpoint, EPacketType packetType, ReadOnlyMemory<byte> data, CancellationToken ct)
{
    byte[] payload = [(byte)packetType, .. data.Span];
    Debug.WriteLine($"SERVER | Sending packet of size {payload.Length} bytes, of type {packetType} to {endpoint}");
    await udpClient.SendAsync(payload, endpoint, ct);
}

async Task SendPacketNoData(IPEndPoint endpoint, EPacketType packetType, CancellationToken ct)
{
    Debug.WriteLine($"SERVER | Sending packet without data of type {packetType} to {endpoint}");

    byte[] payload = [(byte)packetType];
    await udpClient.SendAsync(payload, endpoint, ct);
}

Task SendPublicRSAKey(IPEndPoint endpoint, CancellationToken ct)
    => SendPacket(endpoint, EPacketType.RSAPublicKey, publicKeyBytes, ct);

async Task EncryptAndSendOperation<T>(Client client, EOperation op, T payload, CancellationToken ct) where T : class
{
    if (client.AesKey is null || client.AesIV is null)
        return;
    if (client.Aes is null)
        client.Aes = new(client.AesKey, 16);

    Debug.WriteLine($"SERVER | Encrypting and sending operation {op} with payload {payload.GetType().Name} to {client.RemoteClient?.Name}@{client.Endpoint}");

    using var memory = new MemoryStream();

    var opNum = (ushort)op;
    memory.WriteByte((byte)(opNum >> 8));
    memory.WriteByte((byte)(opNum & 0xFF));

    Serializer.Serialize(memory, payload);

    var encrypted = new byte[memory.Length + 16].AsMemory();
    client.Aes.Encrypt(client.AesIV, memory.ToArray(), encrypted[..^16].Span, encrypted[^16..].Span);

    await SendPacket(client.Endpoint, EPacketType.AESEncryptedData, encrypted, ct);
}

async Task DecryptAndSaveAesKey(Client client, byte[] data, CancellationToken ct)
{
    Debug.WriteLine($"SERVER | Decrypting {client.RemoteClient?.Name}@{client.Endpoint}'s AES key");

    var aesKeyIV = decryptEngine.ProcessBlock(data, 0, data.Length);

    if (client.Aes is not null)
    {
        client.Aes.Dispose();
        client.Aes = null;
    }

    if (aesKeyIV.Length != 44)
        return;

    client.AesKey = aesKeyIV[..^12];
    client.AesIV = aesKeyIV[^12..];
    client.Aes = new(client.AesKey, 16);

    Debug.WriteLine($"SERVER | {client.RemoteClient?.Name}@{client.Endpoint}'s AES key decrypted");

    await SendPacketNoData(client.Endpoint, EPacketType.HandshakeComplete, ct);

    Console.WriteLine($"{client.RemoteClient?.Name}@{client.Endpoint}: Client is performing a handshake");
}

bool TryDecrypt(Client client, ReadOnlyMemory<byte> data, out Memory<byte> decryptedData)
{
    decryptedData = default;

    if (data.Length < 17)
        return false;

    if (client.AesKey is null || client.AesIV is null)
        return false;
    if (client.Aes is null)
        client.Aes = new(client.AesKey, 16);

    decryptedData = new byte[data.Length - 16];
    client.Aes.Decrypt(client.AesIV, data[..^16].Span, data[^16..].Span, decryptedData.Span);

    return true;
}

Task ProcessPacket(Client client, ReadOnlyMemory<byte> data, CancellationToken ct)
{
    if (data.Length < 3)
        return Task.CompletedTask;

    var span = data.Span;
    var op = (EOperation)((span[0] << 8) | span[1]);

    Debug.WriteLine($"SERVER | Processing packet from {client.RemoteClient?.Name} of size {data.Length}, of operation {op}");

    switch (op)
    {
        case EOperation.Punch:
            return Punch(client, data[2..], ct);
        case EOperation.Query:
            return Query(client, data[2..], ct);
        case EOperation.Connect:
            return ConnectRemoteClients(client, data[2..], ct);
    }

    return Task.CompletedTask;
}

async Task Punch(Client client, ReadOnlyMemory<byte> data, CancellationToken ct)
{
    var punch = Serializer.Deserialize<PunchOp>(data);
    if (punch is null)
        return;

    if (client.RemoteClient is null)
    {
        client.RemoteClient = new()
        {
            IpBytes = client.Endpoint.Address.GetAddressBytes(),
            Port = client.Endpoint.Port,
            Token = punch.Token,
            Name = punch.Name,
            Extra = punch.Extra,
            Tags = punch.Tags
        };
        Clients.AddClientToGroup(client.Endpoint, client.RemoteClient.Token ?? string.Empty);
        Console.WriteLine($"{client.RemoteClient.Name}@{client.Endpoint}: Handshake completed");
    }
    else
    {
        if (client.RemoteClient.Token != punch.Token)
        {
            Clients.RemoveClientFromGroup(client.Endpoint, client.RemoteClient.Token ?? string.Empty);
            client.RemoteClient.Token = punch.Token;
            Clients.AddClientToGroup(client.Endpoint, client.RemoteClient.Token ?? string.Empty);
        }

        client.RemoteClient.Name = punch.Name;
        client.RemoteClient.Extra = punch.Extra;
        client.RemoteClient.Tags = punch.Tags;
    }

    Debug.WriteLine($"SERVER | Got punch from {client.RemoteClient.Name}@{client.Endpoint}");

    await EncryptAndSendOperation(client, EOperation.PunchRes, client.RemoteClient, ct);
}

async Task Query(Client client, ReadOnlyMemory<byte> data, CancellationToken ct)
{
    var query = Serializer.Deserialize<QueryOp>(data);
    if (query is null)
        return;

    Debug.WriteLine($"SERVER | Got query from {client.RemoteClient?.Name}@{client.Endpoint} looking for {query.Token} token and {string.Join(",", query.Tags ?? [])} tags");

    var group = Clients.GetGroupByToken(query.Token ?? string.Empty);

    var queryClients = group is null ? [] : query.Tags is null ? group.ToArray() : group.Where(c => c.RemoteClient is not null && query.Tags.All(t => c.RemoteClient.Tags is not null && c.RemoteClient.Tags.Contains(t)));
    var queryRes = new QueryRes { Clients = queryClients.Where(x => x.RemoteClient is not null).Select(x => x.RemoteClient!).ToArray() };
    await EncryptAndSendOperation(client, EOperation.QueryRes, queryRes, ct);
}

async Task ConnectRemoteClients(Client client, ReadOnlyMemory<byte> data, CancellationToken ct)
{
    if (client.RemoteClient is null || client.AesKey is null || client.AesIV is null)
        return;

    var connect = Serializer.Deserialize<ConnectOp>(data);
    if (connect is null)
        return;

    if (!Clients.IsClientExists(connect.EndPoint))
        return;

    var clientToConnect = Clients.GetClientByEndpoint(connect.EndPoint);
    if (clientToConnect.RemoteClient is null)
        return;
    if (clientToConnect.RemoteClient.Token != client.RemoteClient.Token)
        return;

    var p2p = new P2POp
    {
        RemoteClient = client.RemoteClient,
        AesKey = client.AesKey,
        AesIV = client.AesIV
    };

    Debug.WriteLine($"SERVER | Connecting {client.RemoteClient.Name}@{client.Endpoint} to {clientToConnect.RemoteClient.Name}@{clientToConnect.Endpoint}");
    Console.WriteLine($"Connecting {client.RemoteClient.Name}@{client.Endpoint} to {clientToConnect.RemoteClient.Name}@{clientToConnect.Endpoint}");

    await EncryptAndSendOperation(clientToConnect, EOperation.P2P, p2p, ct);
}
