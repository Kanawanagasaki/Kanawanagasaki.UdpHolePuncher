using Kanawanagasaki.UdpHolePuncher.Contracts;
using Kanawanagasaki.UdpHolePuncher.Server;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

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
    await udpClient.SendAsync(payload, endpoint, ct);
}

async Task SendPacketNoData(IPEndPoint endpoint, EPacketType packetType, CancellationToken ct)
{
    byte[] payload = [(byte)packetType];
    await udpClient.SendAsync(payload, endpoint, ct);
}

Task SendPublicRSAKey(IPEndPoint endpoint, CancellationToken ct)
    => SendPacket(endpoint, EPacketType.RSAPublicKey, publicKeyBytes, ct);

async Task EncryptAndSendOperation(Client client, EOperation op, ISerializable payload, CancellationToken ct)
{
    if (client.AesKey is null || client.AesIV is null)
        return;
    if (client.Aes is null)
        client.Aes = new(client.AesKey, AesGcm.TagByteSizes.MaxSize);

    var opNum = (ushort)op;
    var buffer = new byte[2 + payload.GetSerializedSize()];
    buffer[0] = (byte)(opNum >> 8);
    buffer[1] = (byte)(opNum & 0xFF);
    payload.Serialize(buffer.AsSpan(2));

    var encrypted = new byte[buffer.Length + AesGcm.TagByteSizes.MaxSize].AsMemory();
    client.Aes.Encrypt(client.AesIV, buffer, encrypted[..^AesGcm.TagByteSizes.MaxSize].Span, encrypted[^AesGcm.TagByteSizes.MaxSize..].Span);

    await SendPacket(client.Endpoint, EPacketType.AESEncryptedData, encrypted, ct);
}

async Task DecryptAndSaveAesKey(Client client, byte[] encryptedData, CancellationToken ct)
{
    var decryptedData = decryptEngine.ProcessBlock(encryptedData, 0, encryptedData.Length);
    var handshakeOp = HandshakeOp.Deserialize(decryptedData);

    if (client.Aes is not null)
    {
        client.Aes.Dispose();
        client.Aes = null;
    }

    client.AesKey = handshakeOp.AesKey;
    client.AesIV = handshakeOp.AesIV;
    client.Aes = new(client.AesKey, AesGcm.TagByteSizes.MaxSize);

    await SendPacketNoData(client.Endpoint, EPacketType.HandshakeComplete, ct);

    Console.WriteLine($"{client.RemoteClient?.Name}@{client.Endpoint}: Client is performing a handshake");
}

bool TryDecrypt(Client client, ReadOnlyMemory<byte> data, out Memory<byte> decryptedData)
{
    decryptedData = default;

    if (data.Length < AesGcm.TagByteSizes.MaxSize)
        return false;

    if (client.AesKey is null || client.AesIV is null)
        return false;
    if (client.Aes is null)
        client.Aes = new(client.AesKey, AesGcm.TagByteSizes.MaxSize);

    decryptedData = new byte[data.Length - AesGcm.TagByteSizes.MaxSize];
    client.Aes.Decrypt(client.AesIV, data[..^AesGcm.TagByteSizes.MaxSize].Span, data[^AesGcm.TagByteSizes.MaxSize..].Span, decryptedData.Span);

    return true;
}

Task ProcessPacket(Client client, ReadOnlyMemory<byte> data, CancellationToken ct)
{
    if (data.Length < 2)
        return Task.CompletedTask;

    var span = data.Span;
    var op = (EOperation)((span[0] << 8) | span[1]);

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
    var punch = PunchOp.Deserialize(data.Span);
    client.IsQuerable = punch.IsQuerable;

    if (client.RemoteClient is null)
    {
        client.RemoteClient = new()
        {
            IpBytes = client.Endpoint.Address.GetAddressBytes(),
            Port = (ushort)client.Endpoint.Port,
            Project = punch.Project,
            Name = punch.Name,
            Password = punch.Password,
            Extra = punch.Extra,
            Tags = punch.Tags
        };
        Clients.UpdateClientUuid(client);
        Clients.AddClientToGroup(client.Endpoint, client.RemoteClient.Project ?? string.Empty);
        Console.WriteLine($"{client.RemoteClient.Name}@{client.Endpoint}: Handshake completed");
    }
    else
    {
        if (client.RemoteClient.Project != punch.Project)
        {
            Clients.RemoveClientFromGroup(client.Endpoint, client.RemoteClient.Project ?? string.Empty);
            client.RemoteClient.Project = punch.Project;
            Clients.AddClientToGroup(client.Endpoint, client.RemoteClient.Project ?? string.Empty);
        }

        client.RemoteClient.Name = punch.Name;
        client.RemoteClient.Password = punch.Password;
        client.RemoteClient.Extra = punch.Extra;
        client.RemoteClient.Tags = punch.Tags;
    }

    await EncryptAndSendOperation(client, EOperation.PunchRes, client.RemoteClient, ct);
}

async Task Query(Client client, ReadOnlyMemory<byte> data, CancellationToken ct)
{
    var query = QueryOp.Deserialize(data.Span);

    var group = Clients.GetGroupByProject(query.Project ?? string.Empty);

    var queryClients = group is null ? []
                     : query.Tags is null ? group.ToArray()
                     : group.Where(c => c.RemoteClient is not null && query.Tags.All(t => c.RemoteClient.Tags is not null && c.RemoteClient.Tags.Contains(t)));
    queryClients = queryClients.Where(x => x.IsQuerable && x.RemoteClient is not null);

    var queryRes = new QueryRes
    {
        PublicClients = queryClients.Where(x => x.RemoteClient!.Password is null).Select(x => x.RemoteClient!).ToArray(),
        PrivateClients = queryClients.Where(x => x.RemoteClient!.Password is not null).Select(x => x.RemoteClient!.ToMin()).ToArray()
    };

    await EncryptAndSendOperation(client, EOperation.QueryRes, queryRes, ct);
}

async Task ConnectRemoteClients(Client client, ReadOnlyMemory<byte> data, CancellationToken ct)
{
    if (client.RemoteClient is null || client.AesKey is null || client.AesIV is null)
        return;

    var connect = ConnectOp.Deserialize(data.Span);

    var clientToConnect = Clients.GetClientByUuid(connect.Uuid);
    if (clientToConnect is null || clientToConnect.RemoteClient is null || clientToConnect.AesKey is null || clientToConnect.AesIV is null)
        return;
    if (clientToConnect.RemoteClient.Project != client.RemoteClient.Project)
        return;
    if (clientToConnect.RemoteClient.Password != connect.Password)
        return;

    Console.WriteLine($"Connecting {client.RemoteClient.Name}@{client.Endpoint} to {clientToConnect.RemoteClient.Name}@{clientToConnect.Endpoint}");

    {
        var p2p = new P2POp
        {
            RemoteClient = client.RemoteClient,
            AesKey = client.AesKey,
            AesIV = client.AesIV
        };

        await EncryptAndSendOperation(clientToConnect, EOperation.P2P, p2p, ct);
    }

    {
        var p2p = new P2POp
        {
            RemoteClient = clientToConnect.RemoteClient,
            AesKey = clientToConnect.AesKey,
            AesIV = clientToConnect.AesIV
        };

        await EncryptAndSendOperation(client, EOperation.P2P, p2p, ct);
    }
}
