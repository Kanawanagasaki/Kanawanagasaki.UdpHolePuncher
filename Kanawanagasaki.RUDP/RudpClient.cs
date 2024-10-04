namespace Kanawanagasaki.RUDP;

using Kanawanagasaki.UdpHolePuncher.Client;
using Kanawanagasaki.UdpHolePuncher.Contracts;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

public class RudpClient : IDisposable, IAsyncDisposable
{
    public HolePuncherClient HolePuncher { get; }
    public RemoteClient RemoteClient { get; }

    public delegate void OnDatagramEvent(ReadOnlyMemory<byte> data);
    public event OnDatagramEvent? OnDatagram;

    private ConcurrentQueue<byte[]> _stream = new();
    private ConcurrentDictionary<uint, byte[]> _streamOutOfOrderChunks = new();
    private TaskCompletionSource? _streamDataAvailable;
    private int _streamChunkOffset = 0;
    private uint _streamOutgoingOrder = 0;
    private uint _streamIncomingOrder = 0;
    private SemaphoreSlim _streamSemaphore = new(1, 1);

    private ConcurrentDictionary<uint, (long time, byte[] packet)> _reliablePackets = new();

    private uint _outgoingReliablePacketId = 0;
    private uint _lastIncomingReliablePacketId = 0;
    private HashSet<uint> _missingReliablePacketIds = new();
    private object _reliablePacketsLock = new();

    public bool HasMissingReliableDatagrams => 0 < _missingReliablePacketIds.Count;

    public TimeSpan TickPeriod
    {
        get => _tickTimer.Period;
        set => _tickTimer.Period = value;
    }
    private PeriodicTimer _tickTimer;
    private CancellationTokenSource _tickCts;
    private Task _tickTask;

    public bool DebugDropPackets { get; set; } = false;
    public double DebugDropPacketsChance { get; set; } = 0d;

    public RudpClient(HolePuncherClient holePuncher, RemoteClient remoteClient)
    {
        HolePuncher = holePuncher;
        HolePuncher.OnData += HolePuncher_OnData;

        RemoteClient = remoteClient;

        _tickTimer = new(TimeSpan.FromSeconds(2));
        _tickCts = new CancellationTokenSource();
        _tickTask = TickAsync();
    }

    private void HolePuncher_OnData(RemoteClient client, byte[] data)
    {
        if (!RemoteClient.Equals(client))
            return;
        if (data.Length < 1)
            return;

        if (DebugDropPackets && Random.Shared.NextDouble() <= DebugDropPacketsChance)
            return;

        var dataType = (EDataType)data[0];
        if (dataType.HasFlag(EDataType.Datagram))
            OnDatagram?.Invoke(data.AsMemory(1));
        else if (dataType.HasFlag(EDataType.ReliableDatagram))
        {
            var id = (uint)((data[1] << 24) | (data[2] << 16) | (data[3] << 8) | data[4]);

            lock (_reliablePacketsLock)
            {
                if (_lastIncomingReliablePacketId < id)
                {
                    for (uint i = _lastIncomingReliablePacketId + 1; i < id; i++)
                        _missingReliablePacketIds.Add(i);
                    _lastIncomingReliablePacketId = id;

                    if (dataType.HasFlag(EDataType.Stream))
                        HandleStream(id, data.AsMemory(5));
                    else
                        OnDatagram?.Invoke(data.AsMemory(5));
                }
                else if (_missingReliablePacketIds.Contains(id))
                {
                    _missingReliablePacketIds.Remove(id);

                    if (dataType.HasFlag(EDataType.Stream))
                        HandleStream(id, data.AsMemory(5));
                    else
                        OnDatagram?.Invoke(data.AsMemory(5));
                }
            }

            Task.Run(() => SendDatagramInternalAsync(EDataType.ReliableDatagramAck, data[1..5], default));
        }
        else if (dataType.HasFlag(EDataType.ReliableDatagramAck))
        {
            var id = (uint)((data[1] << 24) | (data[2] << 16) | (data[3] << 8) | data[4]);
            _reliablePackets.TryRemove(id, out _);
        }
    }

    private void HandleStream(uint packetId, ReadOnlyMemory<byte> chunk)
    {
        var span = chunk.Span;
        var order = (uint)((span[0] << 24) | (span[1] << 16) | (span[2] << 8) | span[3]);

        var dataAvailable = false;

        if (_streamIncomingOrder + 1 == order)
        {
            _stream.Enqueue(chunk[4..].ToArray());
            _streamIncomingOrder = order;
            dataAvailable = true;
        }
        else if (_streamIncomingOrder < order)
        {
            var data = chunk[4..].ToArray();
            _streamOutOfOrderChunks.AddOrUpdate(order, data, (_, _) => data);
        }
        else
            return;

        while (_streamOutOfOrderChunks.TryRemove(_streamIncomingOrder + 1, out var data))
        {
            _stream.Enqueue(data);
            _streamIncomingOrder++;
            dataAvailable = true;
        }

        if (dataAvailable)
            _streamDataAvailable?.SetResult();
    }

    public async Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_stream.TryPeek(out var chunk))
            {
                if (chunk.Length - _streamChunkOffset <= buffer.Length)
                {
                    var read = chunk.Length - _streamChunkOffset;
                    Buffer.BlockCopy(chunk, _streamChunkOffset, buffer, 0, read);
                    _stream.TryDequeue(out _);
                    _streamChunkOffset = 0;
                    return read;
                }
                else
                {
                    Buffer.BlockCopy(chunk, _streamChunkOffset, buffer, 0, buffer.Length);
                    _streamChunkOffset += buffer.Length;
                    return buffer.Length;
                }
            }

            if (_streamDataAvailable is null || _streamDataAvailable.Task.IsCompleted)
                _streamDataAvailable = new TaskCompletionSource();

            var completedTask = await Task.WhenAny(_streamDataAvailable.Task, Task.Delay(-1, ct), Task.Delay(-1, _tickCts.Token));

            if (completedTask == _streamDataAvailable.Task)
                continue;
            else
                return 0;
        }

        return 0;
    }

    public Task SendDatagramAsync(byte[] data, CancellationToken ct)
        => SendDatagramInternalAsync(EDataType.Datagram, data, ct);

    private Task SendDatagramInternalAsync(EDataType dataType, byte[] data, CancellationToken ct)
        => HolePuncher.SendTo(RemoteClient, [(byte)dataType, .. data], ct);

    public Task SendReliableDatagramAsync(byte[] data, CancellationToken ct)
        => SendReliableDatagramInternalAsync(data, false, ct);

    public async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        await _streamSemaphore.WaitAsync(ct);
        try
        {
            for (int offset = 0; offset < data.Length; offset += 1280)
            {
                var chunkSize = Math.Min(1280, data.Length - offset);

                var order = ++_streamOutgoingOrder;
                byte[] chunk =
                [
                    (byte)((order >> 24) & 0xFF),
                    (byte)((order >> 16) & 0xFF),
                    (byte)((order >> 8) & 0xFF),
                    (byte)(order & 0xFF),
                    ..data[offset..(offset + chunkSize)]
                ];
                await SendReliableDatagramInternalAsync(chunk, true, ct);
            }
        }
        finally
        {
            _streamSemaphore.Release();
        }
    }

    private async Task SendReliableDatagramInternalAsync(byte[] data, bool isStream, CancellationToken ct)
    {
        if (128 < _reliablePackets.Count)
            throw new Exception("Too many outgoing packets awaiting acknowledgement");

        var id = ++_outgoingReliablePacketId;
        byte[] packet =
        [
            isStream ? (byte)(EDataType.ReliableDatagram | EDataType.Stream) : (byte)EDataType.ReliableDatagram,
            (byte)((id >> 24) & 0xFF),
            (byte)((id >> 16) & 0xFF),
            (byte)((id >> 8) & 0xFF),
            (byte)(id & 0xFF),
            ..data
        ];

        var item = (Stopwatch.GetTimestamp(), packet);
        _reliablePackets.AddOrUpdate(id, item, (_, _) => item);
        await HolePuncher.SendTo(RemoteClient, packet, ct);
    }

    private async Task TickAsync()
    {
        try
        {
            while (!_tickCts.IsCancellationRequested && await _tickTimer.WaitForNextTickAsync(_tickCts.Token))
            {
                foreach (var (_, (time, packet)) in _reliablePackets)
                    if (TickPeriod < Stopwatch.GetElapsedTime(time))
                        await HolePuncher.SendTo(RemoteClient, packet, _tickCts.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _tickCts.Cancel();
        await _tickTask;
        _tickCts.Dispose();

        if (_streamDataAvailable is not null && !_streamDataAvailable.Task.IsCompleted)
            _streamDataAvailable.SetResult();

        _streamSemaphore.Dispose();
    }

    public void Dispose()
        => DisposeAsync().GetAwaiter().GetResult();

    ~RudpClient()
    {
        Dispose();
    }
}
