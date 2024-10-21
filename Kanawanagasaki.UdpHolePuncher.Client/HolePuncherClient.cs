namespace Kanawanagasaki.UdpHolePuncher.Client;

using Kanawanagasaki.UdpHolePuncher.Contracts;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Security;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

public class HolePuncherClient : IAsyncDisposable
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

    public bool IsQuerable { get; set; } = false;

    public string Project { get; }
    public string? Name { get; set; }
    public string? Password { get; set; }
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
    private readonly SemaphoreSlim _startSemaphore = new(1, 1);
    private TaskCompletionSource<byte[]>? _serverPublicKeyTcs;
    private TaskCompletionSource? _serverHandshakeCompleteTcs;

    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(2));
    private Task? _tickTask;
    private CancellationTokenSource? _tickCts;

    private Task? _receiveTask;
    private CancellationTokenSource? _receiveCts;

    private TaskCompletionSource<QueryRes?>? _queryResTcs;
    private readonly SemaphoreSlim _queryResSemaphore = new(1, 1);

    private readonly ConcurrentDictionary<Guid, (long timeAdded, string? password)> _toConnect = new();
    private readonly ConcurrentDictionary<IPEndPoint, P2PClient> _connectedClients = new();
    public IEnumerable<RemoteClient> ConnectedClients => _connectedClients.Values.Select(x => x.RemoteClient);

    public HolePuncherClient(IPEndPoint serverEndPoint, string project, Func<RemoteClient, bool> allowClient)
    {
        _serverEndPoint = serverEndPoint;
        Project = project;
        UdpClient = new UdpClient()
        {
            ExclusiveAddressUse = false
        };
        UdpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        _allowClientCallback = allowClient;
    }

    public async Task Start(TimeSpan timeout, CancellationToken ct)
    {
        if (ServerConnectionStatus != EConnectionStatus.Disconnected)
            await Stop(ct);

        await _startSemaphore.WaitAsync();

        try
        {
            _aesKey = RandomNumberGenerator.GetBytes(32);
            _aesIV = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            _aes = new(_aesKey, AesGcm.TagByteSizes.MaxSize);
            ServerConnectionStatus = EConnectionStatus.Handshake;

            _receiveCts = new();
            _receiveTask = Receive();

            if (_serverPublicKeyTcs is not null)
                _serverPublicKeyTcs.TrySetCanceled();
            _serverPublicKeyTcs = new();
            await SendPacket(_serverEndPoint, EPacketType.RSAPublicKey, ct);
            var pubKeyTask = await Task.WhenAny([_serverPublicKeyTcs.Task, Task.Delay(timeout, ct)]);

            if (pubKeyTask != _serverPublicKeyTcs.Task || !_serverPublicKeyTcs.Task.IsCompleted)
            {
                await Stop(ct);
                throw new TimeoutException();
            }

            if (_serverHandshakeCompleteTcs is not null)
                _serverHandshakeCompleteTcs.TrySetCanceled();
            _serverHandshakeCompleteTcs = new();

            var serverPublicKey = await _serverPublicKeyTcs.Task;
            await ProcessServerRSAPublicKey(serverPublicKey, ct);

            var handshakeCompleteTask = await Task.WhenAny([_serverHandshakeCompleteTcs.Task, Task.Delay(timeout, ct)]);

            if (handshakeCompleteTask != _serverHandshakeCompleteTcs.Task || !_serverHandshakeCompleteTcs.Task.IsCompleted)
            {
                await Stop(ct);
                throw new TimeoutException();
            }

            ServerConnectionStatus = EConnectionStatus.Connected;

            _tickCts = new();
            _tickTask = Tick();

            await Punch(ct);
        }
        finally
        {
            _startSemaphore.Release();
        }
    }

    public Task Connect(RemoteClient client, CancellationToken ct)
        => Connect(client.Uuid, client.Password, ct);

    public Task Connect(RemoteClientMin client, string? password, CancellationToken ct)
        => Connect(client.Uuid, password, ct);

    public Task Connect(Guid uuid, string? password, CancellationToken ct)
    {
        _toConnect.AddOrUpdate(uuid, (Stopwatch.GetTimestamp(), password), (_, _) => (Stopwatch.GetTimestamp(), password));
        var connect = new ConnectOp { Uuid = uuid, Password = password };
        return EncryptAndSendOperationToServer(EOperation.Connect, connect, ct);
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

    private Task EncryptAndSendOperationToServer(EOperation op, ISerializable payload, CancellationToken ct)
    {
        if (_aesKey is null || _aesIV is null || _aes is null)
            return Task.CompletedTask;

        var opNum = (ushort)op;
        var buffer = new byte[2 + payload.GetSerializedSize()];
        buffer[0] = (byte)(opNum >> 8);
        buffer[1] = (byte)(opNum & 0xFF);
        payload.Serialize(buffer.AsSpan(2));

        var encrypted = new byte[buffer.Length + AesGcm.TagByteSizes.MaxSize].AsMemory();
        _aes.Encrypt(_aesIV, buffer, encrypted[..^AesGcm.TagByteSizes.MaxSize].Span, encrypted[^AesGcm.TagByteSizes.MaxSize..].Span);

        return SendPacket(_serverEndPoint, EPacketType.AESEncryptedData, encrypted, ct);
    }

    private Task EncryptAndSendOperation(P2PClient p2p, EOperation op, CancellationToken ct)
    {
        var opNum = (ushort)op;
        var data = new byte[] { (byte)(opNum >> 8), (byte)(opNum & 0xFF) };
        var encrypted = new byte[2 + AesGcm.TagByteSizes.MaxSize].AsMemory();
        p2p.Aes.Encrypt(p2p.AesIV, data, encrypted[..^AesGcm.TagByteSizes.MaxSize].Span, encrypted[^AesGcm.TagByteSizes.MaxSize..].Span);

        return SendPacket(p2p.EndPoint, EPacketType.AESEncryptedData, encrypted, ct);
    }

    private Task EncryptAndSendOperation(P2PClient p2p, EOperation op, ISerializable payload, CancellationToken ct)
    {
        if (_aesKey is null || _aesIV is null || _aes is null)
            return Task.CompletedTask;

        var opNum = (ushort)op;
        var buffer = new byte[2 + payload.GetSerializedSize()];
        buffer[0] = (byte)(opNum >> 8);
        buffer[1] = (byte)(opNum & 0xFF);
        payload.Serialize(buffer.AsSpan(2));

        var encrypted = new byte[buffer.Length + AesGcm.TagByteSizes.MaxSize].AsMemory();
        p2p.Aes.Encrypt(p2p.AesIV, buffer, encrypted[..^AesGcm.TagByteSizes.MaxSize].Span, encrypted[^AesGcm.TagByteSizes.MaxSize..].Span);

        return SendPacket(p2p.EndPoint, EPacketType.AESEncryptedData, encrypted, ct);
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

        var encrypted = new byte[memory.Length + AesGcm.TagByteSizes.MaxSize].AsMemory();
        p2p.Aes.Encrypt(p2p.AesIV, memory.ToArray(), encrypted[..^AesGcm.TagByteSizes.MaxSize].Span, encrypted[^AesGcm.TagByteSizes.MaxSize..].Span);

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
                    if (ServerConnectionStatus != EConnectionStatus.Connected)
                        break;
                    if (_tickCts is null)
                        break;

                    var ct = _tickCts.Token;
                    await Punch(ct);

                    foreach (var (uuid, (timeAdded, password)) in _toConnect)
                    {
                        if (Stopwatch.GetElapsedTime(timeAdded) < InactiveClientsDisconnectTimeSpan)
                        {
                            var connect = new ConnectOp { Uuid = uuid, Password = password };
                            await EncryptAndSendOperationToServer(EOperation.Connect, connect, ct);
                        }
                        else
                            _toConnect.TryRemove(uuid, out _);
                    }

                    foreach (var (endpoint, p2pClient) in _connectedClients)
                    {
                        if (Stopwatch.GetElapsedTime(p2pClient.LastTimePing) < InactiveClientsDisconnectTimeSpan)
                        {
                            if (p2pClient.ConnectionStatus == EConnectionStatus.Handshake)
                                await EncryptAndSendOperation(p2pClient, EOperation.Handshake, ct);
                            else
                                await SendPacket(endpoint, EPacketType.Ping, ct);
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
            IsQuerable = IsQuerable,
            Project = Project,
            Name = Name,
            Password = Password,
            Extra = Extra,
            Tags = Tags
        };

        await EncryptAndSendOperationToServer(EOperation.Punch, punch, ct);
    }

    public async Task<QueryRes?> Query(string[]? tags, TimeSpan timeout, CancellationToken ct)
    {
        await _queryResSemaphore.WaitAsync();
        try
        {
            _queryResTcs = new();

            var query = new QueryOp { Project = Project, Tags = tags };
            await EncryptAndSendOperationToServer(EOperation.Query, query, ct);
            await Task.WhenAny([_queryResTcs.Task, Task.Delay(timeout, ct)]);

            var res = _queryResTcs.Task.IsCompletedSuccessfully ? await _queryResTcs.Task : null;

            _queryResTcs = null;

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
        while (_receiveCts is not null && !_receiveCts.IsCancellationRequested && ServerConnectionStatus != EConnectionStatus.Disconnected)
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
                        if (!remoteEndpoint.Equals(_serverEndPoint))
                            break;
                        if (_serverPublicKeyTcs is not null)
                            _serverPublicKeyTcs.TrySetResult(receiveRes.Buffer[1..]);
                        break;
                    case EPacketType.HandshakeComplete:
                        if (!remoteEndpoint.Equals(_serverEndPoint))
                            break;
                        if (_serverHandshakeCompleteTcs is not null)
                            _serverHandshakeCompleteTcs.TrySetResult();
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
            _aesIV = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var handshakeOp = new HandshakeOp { AesKey = _aesKey, AesIV = _aesIV };
        var buffer = new byte[handshakeOp.GetSerializedSize()];
        handshakeOp.Serialize(buffer);
        var encryptedAesKey = encryptEngine.ProcessBlock(buffer, 0, buffer.Length);
        return SendPacket(_serverEndPoint, EPacketType.RSAEncryptedAESKey, encryptedAesKey, ct);
    }

    private bool TryDecrypt(ReadOnlyMemory<byte> data, out Memory<byte> decryptedData)
    {
        decryptedData = default;

        if (_aesKey is null || _aesIV is null || _aes is null)
            return false;

        if (data.Length < AesGcm.TagByteSizes.MaxSize)
            return false;

        decryptedData = new byte[data.Length - AesGcm.TagByteSizes.MaxSize];
        _aes.Decrypt(_aesIV, data[..^AesGcm.TagByteSizes.MaxSize].Span, data[^AesGcm.TagByteSizes.MaxSize..].Span, decryptedData.Span);

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
                    PunchResult = RemoteClient.Deserialize(data[2..].Span);
                    break;
                }
            case EOperation.QueryRes:
                {
                    QueryRes? queryRes;
                    try
                    {
                        queryRes = QueryRes.Deserialize(data[2..].Span);
                    }
                    catch
                    {
                        queryRes = null;
                    }

                    if (_queryResTcs is not null)
                        _queryResTcs.TrySetResult(queryRes);
                    break;
                }
            case EOperation.P2P:
                {
                    if (!endpoint.Equals(_serverEndPoint))
                        break;

                    if (_aesKey is null || _aesIV is null)
                        break;

                    var p2p = P2POp.Deserialize(data[2..].Span);
                    if (!_allowClientCallback(p2p.RemoteClient))
                        break;

                    var p2pClient = _connectedClients.GetValueOrDefault(p2p.RemoteClient.EndPoint);
                    if (p2pClient is null)
                    {
                        p2pClient = new P2PClient(p2p.RemoteClient, p2p.AesKey, p2p.AesIV)
                        {
                            ConnectionStatus = EConnectionStatus.Handshake,
                            LastTimePing = Stopwatch.GetTimestamp()
                        };
                        _connectedClients.AddOrUpdate(p2pClient.EndPoint, p2pClient, (_, _) => p2pClient);
                    }

                    await EncryptAndSendOperation(p2pClient, EOperation.Handshake, ct);

                    break;
                }
            case EOperation.Handshake:
                {
                    if (_connectedClients.TryGetValue(endpoint, out var p2pClient))
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
                        _toConnect.TryRemove(p2pClient.RemoteClient.Uuid, out _);
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
        _toConnect.TryRemove(client.Uuid, out _);

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

        _serverPublicKeyTcs?.TrySetCanceled();
        _serverPublicKeyTcs = null;

        _serverHandshakeCompleteTcs?.TrySetCanceled();
        _serverHandshakeCompleteTcs = null;

        _queryResTcs?.TrySetCanceled();
        _queryResTcs = null;

        PunchResult = null;
        ServerConnectionStatus = EConnectionStatus.Disconnected;
    }

    public async ValueTask DisposeAsync()
    {
        await Stop(default);
        UdpClient.Dispose();
    }
}
