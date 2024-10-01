namespace Kanawanagasaki.UdpHolePuncher.Client;

using Kanawanagasaki.UdpHolePuncher.Contracts;
using ProtoBuf;
using System;
using System.Net;
using System.Net.Sockets;
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

    public string Token { get; }
    public string? Name { get; set; }
    public byte[]? Extra { get; set; }
    public string[] Tags { get; set; } = [];

    public UdpClient UdpClient { get; }

    public RemoteClient? PunchRes { get; private set; }

    private readonly IPEndPoint _serverEndPoint;

    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(2));
    private Task? _timerTask;
    private CancellationTokenSource? _timerCts;

    private Task? _receiveTask;
    private CancellationTokenSource? _receiveCts;

    private TaskCompletionSource<QueryRes?>? _queryResTask;
    private readonly SemaphoreSlim _queryResSemaphore = new(1, 1);

    private readonly HashSet<RemoteClient> _connectedClients = new();
    public IReadOnlySet<RemoteClient> ConnectedClients => _connectedClients;

    public HolePuncherClient(IPEndPoint serverEndPoint, string token)
    {
        _serverEndPoint = serverEndPoint;
        Token = token;
        UdpClient = new UdpClient()
        {
            ExclusiveAddressUse = false
        };
    }

    public void Start()
    {
        _timerCts = new();
        _timerTask = Tick();

        _receiveCts = new();
        _receiveTask = Receive();
    }

    public async Task Connect(RemoteClient client)
    {
        _connectedClients.Add(client);
        OnRemoteClientConnected?.Invoke(client);

        var connect = new ConnectOp { IpBytes = client.IpBytes, Port = client.Port, Token = client.Token };

        var opNum = (ushort)EOperation.Connect;

        using var memory = new MemoryStream();
        memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
        memory.WriteByte((byte)(opNum >> 8));
        memory.WriteByte((byte)(opNum & 0xFF));
        Serializer.Serialize(memory, connect);

        await UdpClient.SendAsync(memory.ToArray(), _serverEndPoint);

        await Ping(client);
    }

    public async Task SendTo(RemoteClient client, byte[] data)
    {
        if (0x7FFF < data.Length + 6)
            throw new Exception($"Data exceeded maximum length of {0x7FFF} bytes");

        var opNum = (ushort)EOperation.Data;

        using var memory = new MemoryStream();
        memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
        memory.WriteByte((byte)(opNum >> 8));
        memory.WriteByte((byte)(opNum & 0xFF));
        memory.Write(data);

        await UdpClient.SendAsync(memory.ToArray(), client.EndPoint);
    }

    public async Task Disconnect(RemoteClient client)
    {
        _connectedClients.Remove(client);
        OnRemoteClientDisconnected?.Invoke(client);

        var opNum = (ushort)EOperation.Disconnect;

        using var memory = new MemoryStream();
        memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
        memory.WriteByte((byte)(opNum >> 8));
        memory.WriteByte((byte)(opNum & 0xFF));

        await UdpClient.SendAsync(memory.ToArray(), client.EndPoint);
    }

    private async Task Tick()
    {
        if (_timerCts is null)
            throw new NullReferenceException();

        try
        {
            do
            {
                var punch = new PunchOp
                {
                    Token = Token,
                    Name = Name,
                    Extra = Extra,
                    Tags = Tags
                };

                var opNum = (ushort)EOperation.Punch;

                using var memory = new MemoryStream();
                memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
                memory.WriteByte((byte)(opNum << 8));
                memory.WriteByte((byte)(opNum & 0xFF));
                Serializer.Serialize(memory, punch);

                await UdpClient.SendAsync(memory.ToArray(), _serverEndPoint);

                foreach (var remoteClient in _connectedClients)
                    await Ping(remoteClient);
            }
            while (await _timer.WaitForNextTickAsync(_timerCts.Token));
        }
        catch (OperationCanceledException) { }
    }

    public async Task<QueryRes?> Query(string[] tags, TimeSpan timeout)
    {
        try
        {
            await _queryResSemaphore.WaitAsync();

            _queryResTask = new();

            var query = new QueryOp { Token = Token, Tags = tags };

            var opNum = (ushort)EOperation.Query;

            using var memory = new MemoryStream();
            memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
            memory.WriteByte((byte)(opNum >> 8));
            memory.WriteByte((byte)(opNum & 0xFF));
            Serializer.Serialize(memory, query);

            await UdpClient.SendAsync(memory.ToArray(), _serverEndPoint);
            await Task.WhenAny([_queryResTask.Task, Task.Delay(timeout)]);

            var res = _queryResTask.Task.IsCompletedSuccessfully ? await _queryResTask.Task : null;

            _queryResTask = null;

            return res;
        }
        catch
        {
            return null;
        }
        finally
        {
            _queryResSemaphore.Release();
        }
    }

    private async Task Receive()
    {
        if (_receiveCts is null)
            throw new NullReferenceException();

        try
        {
            while (!_receiveCts.IsCancellationRequested)
            {
                var receiveRes = await UdpClient.ReceiveAsync(_receiveCts.Token);
                var bytes = receiveRes.Buffer;
                if (0x7FFF < bytes.Length)
                    continue;
                if (bytes.Length < 6)
                    continue;
                if (bytes[0] != 'K' || bytes[1] != 'U' || bytes[2] != 'H' || bytes[3] != 'P')
                    continue;

                var op = (EOperation)((bytes[4] << 8) | bytes[5]);
                switch (op)
                {
                    case EOperation.PunchRes:
                        {
                            PunchRes = Serializer.Deserialize<RemoteClient>(bytes.AsSpan(6));
                            break;
                        }
                    case EOperation.QueryRes:
                        {
                            QueryRes? queryRes;
                            try
                            {
                                queryRes = Serializer.Deserialize<QueryRes>(bytes.AsSpan(6));
                            }
                            catch
                            {
                                queryRes = null;
                            }

                            if (_queryResTask is not null && !_queryResTask.Task.IsCompleted)
                                _queryResTask.SetResult(queryRes);
                            break;
                        }
                    case EOperation.Connect:
                        {
                            var remoteClient = Serializer.Deserialize<RemoteClient>(bytes.AsSpan(6));
                            if (remoteClient is null)
                                break;

                            _connectedClients.Add(remoteClient);
                            OnRemoteClientConnected?.Invoke(remoteClient);

                            await Ping(remoteClient);

                            break;
                        }
                    case EOperation.Disconnect:
                        {
                            var client = _connectedClients.FirstOrDefault(x => x.EndPoint.Equals(receiveRes.RemoteEndPoint));
                            if (client is null)
                                break;

                            _connectedClients.Remove(client);
                            OnRemoteClientDisconnected?.Invoke(client);

                            break;
                        }
                    case EOperation.Ping:
                        {
                            var opNum = (ushort)EOperation.Pong;

                            using var memory = new MemoryStream();
                            memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
                            memory.WriteByte((byte)(opNum << 8));
                            memory.WriteByte((byte)(opNum & 0xFF));

                            await UdpClient.SendAsync(memory.ToArray(), receiveRes.RemoteEndPoint);

                            break;
                        }
                    case EOperation.Data:
                        {
                            var client = _connectedClients.FirstOrDefault(x => x.EndPoint.Equals(receiveRes.RemoteEndPoint));
                            if (client is not null)
                                OnData?.Invoke(client, bytes[6..]);
                            else
                            {
                                var opNum = (ushort)EOperation.Disconnect;

                                using var memory = new MemoryStream();
                                memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
                                memory.WriteByte((byte)(opNum >> 8));
                                memory.WriteByte((byte)(opNum & 0xFF));

                                await UdpClient.SendAsync(memory.ToArray(), receiveRes.RemoteEndPoint);
                            }

                            break;
                        }
                }
            }
        }
        catch { }
    }

    private async Task Ping(RemoteClient client)
    {
        var opNum = (ushort)EOperation.Ping;

        using var memory = new MemoryStream();
        memory.Write([(byte)'K', (byte)'U', (byte)'H', (byte)'P']);
        memory.WriteByte((byte)(opNum << 8));
        memory.WriteByte((byte)(opNum & 0xFF));

        await UdpClient.SendAsync(memory.ToArray(), _serverEndPoint);
    }

    public async Task Stop()
    {
        foreach (var client in _connectedClients.ToArray())
            await Disconnect(client);

        if (_timerTask is not null)
        {
            _timerCts?.Cancel();
            await _timerTask;
            _timerCts?.Dispose();
            _timerTask = null;
            _timerCts = null;
        }

        if (_receiveTask is not null)
        {
            _receiveCts?.Cancel();
            await _receiveTask;
            _receiveCts?.Dispose();
            _receiveTask = null;
            _receiveCts = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Stop();
    }
}
