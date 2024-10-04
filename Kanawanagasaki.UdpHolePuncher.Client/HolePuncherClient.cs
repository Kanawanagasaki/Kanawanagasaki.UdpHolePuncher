namespace Kanawanagasaki.UdpHolePuncher.Client;

using Kanawanagasaki.UdpHolePuncher.Contracts;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using ProtoBuf;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

public class HolePuncherClient : IDisposable, IAsyncDisposable
{
    public delegate void OnRemoteClientConnectedEvent(RemoteClient client);
    public event OnRemoteClientConnectedEvent? OnRemoteClientConnected;
    public delegate void OnDataEvent(RemoteClient client, byte[] data);
    public event OnDataEvent? OnData;
    public delegate void OnRemoteClientDisconnectedEvent(RemoteClient client);
    public event OnRemoteClientDisconnectedEvent? OnRemoteClientDisconnected;

    public TimeSpan TickPeriod
    {
        get => _timer.Period;
        set => _timer.Period = value;
    }
    public TimeSpan InactiveClientsDisconnectTimeSpan { get; set; } = TimeSpan.FromSeconds(90);

    public string Token { get; }
    public string? Name { get; set; }
    public byte[]? Extra { get; set; }
    public string[] Tags { get; set; } = [];

    private byte[]? _aesKey;
    private byte[]? _aesIV;
    private AesGcm? _aes;

    private Func<RemoteClient, bool> _allowClientCallback;

    public UdpClient UdpClient { get; }

    public RemoteClient? PunchResult { get; private set; }

    private readonly IPEndPoint _serverEndPoint;
    public EConnectionStatus ServerConnectionStatus { get; private set; } = EConnectionStatus.Disconnected;

    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(2));
    private Task? _tickTask;
    private CancellationTokenSource? _tickCts;

    private Task? _receiveTask;
    private CancellationTokenSource? _receiveCts;

    private TaskCompletionSource<QueryRes?>? _queryResTask;
    private readonly SemaphoreSlim _queryResSemaphore = new(1, 1);

    private readonly ConcurrentDictionary<IPEndPoint, (RemoteClient remoteClient, long addedTime)> _toConnect = new();
    private readonly ConcurrentDictionary<IPEndPoint, P2PClient> _connectedClients = new();
    public IEnumerable<RemoteClient> ConnectedClients => _connectedClients.Values.Select(x => x.RemoteClient);

    public HolePuncherClient(IPEndPoint serverEndPoint, string token, Func<RemoteClient, bool> allowClient)
    {
        _serverEndPoint = serverEndPoint;
        Token = token;
        UdpClient = new UdpClient()
        {
            ExclusiveAddressUse = false
        };
        UdpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        _allowClientCallback = allowClient;
    }

    public async Task Start(CancellationToken ct)
    {
        if (ServerConnectionStatus != EConnectionStatus.Disconnected)
            await Stop(ct);

        _aesKey = RandomNumberGenerator.GetBytes(32);
        _aesIV = RandomNumberGenerator.GetBytes(12);
        _aes = new(_aesKey, 16);

        ServerConnectionStatus = EConnectionStatus.Handshake;

        _receiveCts = new();
        _receiveTask = Receive();

        _tickCts = new();
        _tickTask = Tick();
    }

    public async Task Connect(RemoteClient client, CancellationToken ct)
    {
        _toConnect.AddOrUpdate(client.EndPoint, (client, Stopwatch.GetTimestamp()), (_, _) => (client, Stopwatch.GetTimestamp()));

        var connect = new ConnectOp { IpBytes = client.IpBytes, Port = client.Port };
        await EncryptAndSendOperationToServer(EOperation.Connect, connect, ct);
        await SendPacket(client.EndPoint, EPacketType.Ping, ct);
    }

    private async Task SendPacket(IPEndPoint endpoint, EPacketType packetType, CancellationToken ct)
    {
        byte[] payload = [(byte)packetType];
        await UdpClient.SendAsync(payload, endpoint, ct);
    }

    private async Task SendPacket(IPEndPoint endpoint, EPacketType packetType, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        byte[] payload = [(byte)packetType, .. data.Span];
        await UdpClient.SendAsync(payload, endpoint, ct);
    }

    private Task EncryptAndSendOperationToServer<T>(EOperation op, T payload, CancellationToken ct) where T : class
    {
        if (_aesKey is null || _aesIV is null || _aes is null)
            return Task.CompletedTask;

        using var memory = new MemoryStream();

        var opNum = (ushort)op;
        memory.WriteByte((byte)(opNum >> 8));
        memory.WriteByte((byte)(opNum & 0xFF));

        Serializer.Serialize(memory, payload);

        var encrypted = new byte[memory.Length + 16].AsMemory();
        _aes.Encrypt(_aesIV, memory.ToArray(), encrypted[..^16].Span, encrypted[^16..].Span);

        return SendPacket(_serverEndPoint, EPacketType.AESEncryptedData, encrypted, ct);
    }

    private Task EncryptAndSendOperation(P2PClient p2p, EOperation op, CancellationToken ct)
    {
        var opNum = (ushort)op;
        var data = new byte[] { (byte)(opNum >> 8), (byte)(opNum & 0xFF) };
        var encrypted = new byte[18].AsMemory();
        p2p.Aes.Encrypt(p2p.AesIV, data, encrypted[..^16].Span, encrypted[^16..].Span);

        return SendPacket(p2p.EndPoint, EPacketType.AESEncryptedData, encrypted, ct);
    }

    private async Task EncryptAndSendOperation<T>(P2PClient p2p, EOperation op, T payload, CancellationToken ct) where T : class
    {
        using var memory = new MemoryStream();

        var opNum = (ushort)op;
        memory.WriteByte((byte)(opNum >> 8));
        memory.WriteByte((byte)(opNum & 0xFF));

        Serializer.Serialize(memory, payload);

        var encrypted = new byte[memory.Length + 16].AsMemory();
        p2p.Aes.Encrypt(p2p.AesIV, memory.ToArray(), encrypted[..^16].Span, encrypted[^16..].Span);

        await SendPacket(p2p.EndPoint, EPacketType.AESEncryptedData, encrypted, ct);
    }

    public async Task SendTo(RemoteClient client, byte[] data, CancellationToken ct)
    {
        if (!_connectedClients.TryGetValue(client.EndPoint, out var p2p))
            return;

        var opNum = (ushort)EOperation.Data;

        using var memory = new MemoryStream();
        memory.WriteByte((byte)(opNum >> 8));
        memory.WriteByte((byte)(opNum & 0xFF));
        memory.Write(data);

        var encrypted = new byte[memory.Length + 16].AsMemory();
        p2p.Aes.Encrypt(p2p.AesIV, memory.ToArray(), encrypted[..^16].Span, encrypted[^16..].Span);
        
        await SendPacket(p2p.EndPoint, EPacketType.AESEncryptedData, encrypted, ct);
    }

    private async Task Tick()
    {
        try
        {
            do
            {
                try
                {
                    if (_tickCts is null)
                        break;

                    var ct = _tickCts.Token;
                    if (ServerConnectionStatus == EConnectionStatus.Handshake)
                    {
                        await SendPacket(_serverEndPoint, EPacketType.RSAPublicKey, ct);
                        continue;
                    }
                    else if (ServerConnectionStatus != EConnectionStatus.Connected)
                        continue;

                    await Punch(ct);

                    foreach (var (endpoint, (remoteClient, timeAdded)) in _toConnect.ToArray())
                    {
                        if (Stopwatch.GetElapsedTime(timeAdded) < InactiveClientsDisconnectTimeSpan)
                        {
                            var connect = new ConnectOp { IpBytes = remoteClient.IpBytes, Port = remoteClient.Port };
                            await EncryptAndSendOperationToServer(EOperation.Connect, connect, ct);
                            await SendPacket(endpoint, EPacketType.Ping, ct);
                        }
                        else
                            _toConnect.TryRemove(endpoint, out _);
                    }

                    foreach (var (endpoint, p2pClient) in _connectedClients.ToArray())
                    {
                        if (Stopwatch.GetElapsedTime(p2pClient.LastTimePing) < InactiveClientsDisconnectTimeSpan)
                        {
                            await SendPacket(endpoint, EPacketType.Ping, ct);

                            if (p2pClient.ConnectionStatus == EConnectionStatus.Handshake)
                            {
                                var handshake = new HandshakeOp
                                {
                                    AesKey = _aesKey,
                                    AesIV = _aesIV,
                                };

                                await EncryptAndSendOperation(p2pClient, EOperation.Handshake, handshake, ct);
                            }
                        }
                        else
                        {
                            _connectedClients.TryRemove(endpoint, out _);
                            await SendPacket(endpoint, EPacketType.Disconnect, ct);
                            OnRemoteClientDisconnected?.Invoke(p2pClient.RemoteClient);
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }
            while (_tickCts is not null && await _timer.WaitForNextTickAsync(_tickCts.Token));
        }
        catch (OperationCanceledException) { }
    }

    private async Task Punch(CancellationToken ct)
    {
        var punch = new PunchOp
        {
            Token = Token,
            Name = Name,
            Extra = Extra,
            Tags = Tags
        };

        await EncryptAndSendOperationToServer(EOperation.Punch, punch, ct);
    }

    public async Task<QueryRes?> Query(string[]? tags, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await _queryResSemaphore.WaitAsync();

            _queryResTask = new();

            var query = new QueryOp { Token = Token, Tags = tags };
            await EncryptAndSendOperationToServer(EOperation.Query, query, ct);
            await Task.WhenAny([_queryResTask.Task, Task.Delay(timeout, ct)]);

            var res = _queryResTask.Task.IsCompletedSuccessfully ? await _queryResTask.Task : null;

            _queryResTask = null;

            return res;
        }
        catch (Exception e)
        {
            Debug.WriteLine(nameof(HolePuncherClient) + " | " + e.Message);
            Debug.WriteLine(nameof(HolePuncherClient) + " | " + e.StackTrace);

            return null;
        }
        finally
        {
            _queryResSemaphore.Release();
        }
    }

    private async Task Receive()
    {
        while (_receiveCts is not null && !_receiveCts.IsCancellationRequested)
        {
            var ct = _receiveCts.Token;
            try
            {
                var receiveRes = await UdpClient.ReceiveAsync(ct);
                if (receiveRes.Buffer.Length < 1)
                    continue;

                var memoryBytes = receiveRes.Buffer.AsMemory();
                var remoteEndpoint = receiveRes.RemoteEndPoint;

                switch ((EPacketType)receiveRes.Buffer[0])
                {
                    case EPacketType.RSAPublicKey:
                        await ProcessServerRSAPublicKey(memoryBytes[1..], ct);
                        break;
                    case EPacketType.HandshakeComplete:
                        ServerConnectionStatus = EConnectionStatus.Connected;
                        await Punch(ct);
                        break;
                    case EPacketType.AESEncryptedData:
                        if (TryDecrypt(memoryBytes[1..], out var decrypted))
                            await ProcessPacket(remoteEndpoint, decrypted, ct);
                        break;
                    case EPacketType.Ping:
                        {
                            await SendPacket(remoteEndpoint, EPacketType.Pong, ct);
                            if (_connectedClients.TryGetValue(remoteEndpoint, out var p2pClient))
                                p2pClient.LastTimePing = Stopwatch.GetTimestamp();
                            break;
                        }
                    case EPacketType.Pong:
                        {
                            if (_connectedClients.TryGetValue(remoteEndpoint, out var p2pClient))
                                p2pClient.LastTimePing = Stopwatch.GetTimestamp();
                            break;
                        }
                    case EPacketType.Disconnect:
                        {
                            _toConnect.TryRemove(remoteEndpoint, out _);
                            if (_connectedClients.TryRemove(remoteEndpoint, out var p2pClient))
                                OnRemoteClientDisconnected?.Invoke(p2pClient.RemoteClient);
                            break;
                        }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.WriteLine(nameof(HolePuncherClient) + " | " + e.Message);
                Debug.WriteLine(nameof(HolePuncherClient) + " | " + e.StackTrace);
            }
        }
    }

    private Task ProcessServerRSAPublicKey(Memory<byte> data, CancellationToken ct)
    {
        var publicKeyInfo = PublicKeyFactory.CreateKey(SubjectPublicKeyInfo.GetInstance(data.ToArray()));
        var encryptEngine = new Pkcs1Encoding(new RsaEngine());
        encryptEngine.Init(true, publicKeyInfo);

        if (_aesKey is null)
            _aesKey = RandomNumberGenerator.GetBytes(32);
        if (_aesIV is null)
            _aesIV = RandomNumberGenerator.GetBytes(12);
        byte[] payload = [.. _aesKey, .. _aesIV];
        var encryptedAesKey = encryptEngine.ProcessBlock(payload, 0, payload.Length);
        return SendPacket(_serverEndPoint, EPacketType.RSAEncryptedAESKey, encryptedAesKey, ct);
    }

    private bool TryDecrypt(ReadOnlyMemory<byte> data, out Memory<byte> decryptedData)
    {
        decryptedData = default;

        if (_aesKey is null || _aesIV is null || _aes is null)
            return false;

        if (data.Length < 17)
            return false;

        decryptedData = new byte[data.Length - 16];
        _aes.Decrypt(_aesIV, data[..^16].Span, data[^16..].Span, decryptedData.Span);

        return true;
    }

    async Task ProcessPacket(IPEndPoint endpoint, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (data.Length < 2)
            return;

        var op = (EOperation)((data.Span[0] << 8) | data.Span[1]);

        switch (op)
        {
            case EOperation.PunchRes:
                {
                    PunchResult = Serializer.Deserialize<RemoteClient>(data[2..]);
                    break;
                }
            case EOperation.QueryRes:
                {
                    QueryRes? queryRes;
                    try
                    {
                        queryRes = Serializer.Deserialize<QueryRes>(data[2..]);
                    }
                    catch
                    {
                        queryRes = null;
                    }

                    if (_queryResTask is not null && !_queryResTask.Task.IsCompleted)
                        _queryResTask.SetResult(queryRes);
                    break;
                }
            case EOperation.P2P:
                {
                    if (_aesKey is null || _aesIV is null)
                        break;

                    var p2p = Serializer.Deserialize<P2POp>(data[2..]);
                    if (p2p?.RemoteClient is null || p2p?.AesKey is null || p2p?.AesIV is null)
                        break;

                    if (!_allowClientCallback(p2p.RemoteClient))
                        break;

                    var p2pClient = new P2PClient(p2p.RemoteClient, p2p.AesKey, p2p.AesIV)
                    {
                        ConnectionStatus = EConnectionStatus.Handshake,
                        LastTimePing = Stopwatch.GetTimestamp()
                    };
                    _connectedClients.AddOrUpdate(p2pClient.EndPoint, p2pClient, (_, _) => p2pClient);

                    var handshake = new HandshakeOp
                    {
                        AesKey = _aesKey,
                        AesIV = _aesIV,
                    };

                    await EncryptAndSendOperation(p2pClient, EOperation.Handshake, handshake, ct);

                    break;
                }
            case EOperation.Handshake:
                {
                    if (_connectedClients.TryGetValue(endpoint, out var connectedP2PClient))
                    {
                        await EncryptAndSendOperation(connectedP2PClient, EOperation.HandshakeAck, ct);
                        break;
                    }

                    if (!_toConnect.TryRemove(endpoint, out var toConnect))
                        break;

                    var handshake = Serializer.Deserialize<HandshakeOp>(data[2..]);
                    if (handshake?.AesKey is null || handshake?.AesIV is null)
                        break;

                    var p2pClient = _connectedClients.GetValueOrDefault(endpoint);
                    if (p2pClient is null)
                    {
                        p2pClient = new P2PClient(toConnect.remoteClient, handshake.AesKey, handshake.AesIV)
                        {
                            ConnectionStatus = EConnectionStatus.Connected,
                            LastTimePing = Stopwatch.GetTimestamp()
                        };
                        _connectedClients.AddOrUpdate(endpoint, p2pClient, (_, _) => p2pClient);
                        OnRemoteClientConnected?.Invoke(p2pClient.RemoteClient);
                    }

                    await EncryptAndSendOperation(p2pClient, EOperation.HandshakeAck, ct);

                    break;
                }
            case EOperation.HandshakeAck:
                {
                    if (_connectedClients.TryGetValue(endpoint, out var p2pClient))
                    {
                        if (p2pClient.ConnectionStatus != EConnectionStatus.Connected)
                        {
                            p2pClient.ConnectionStatus = EConnectionStatus.Connected;
                            OnRemoteClientConnected?.Invoke(p2pClient.RemoteClient);
                        }
                    }

                    break;
                }
            case EOperation.Data:
                {
                    if (!_connectedClients.TryGetValue(endpoint, out var p2pClient))
                        break;

                    OnData?.Invoke(p2pClient.RemoteClient, data[2..].ToArray());

                    break;
                }
        }

        return;
    }

    public async Task Disconnect(RemoteClient client, CancellationToken ct)
    {
        if (_connectedClients.TryRemove(client.EndPoint, out var p2pClient))
        {
            await SendPacket(p2pClient.EndPoint, EPacketType.Disconnect, ct);
            OnRemoteClientDisconnected?.Invoke(p2pClient.RemoteClient);
        }
    }

    public async Task Stop(CancellationToken ct)
    {
        await SendPacket(_serverEndPoint, EPacketType.Disconnect, ct);

        foreach (var p2p in _connectedClients.Values)
            await Disconnect(p2p.RemoteClient, ct);

        if (_tickTask is not null)
        {
            _tickCts?.Cancel();
            await _tickTask;
            _tickCts?.Dispose();
            _tickTask = null;
            _tickCts = null;
        }

        if (_receiveTask is not null)
        {
            _receiveCts?.Cancel();
            await _receiveTask;
            _receiveCts?.Dispose();
            _receiveTask = null;
            _receiveCts = null;
        }

        PunchResult = null;
        ServerConnectionStatus = EConnectionStatus.Disconnected;
    }

    public void Dispose()
    {
        Stop(default).GetAwaiter().GetResult();
        UdpClient.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Stop(default);
        UdpClient.Dispose();
    }

    ~HolePuncherClient()
    {
        Dispose();
    }
}
