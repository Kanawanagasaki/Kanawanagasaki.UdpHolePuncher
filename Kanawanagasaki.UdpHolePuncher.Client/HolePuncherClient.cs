﻿namespace Kanawanagasaki.UdpHolePuncher.Client;

using Kanawanagasaki.UdpHolePuncher.Contracts;
using ProtoBuf;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public class HolePuncherClient : IAsyncDisposable
{
    public TimeSpan PunchIntervals
    {
        get => _timer.Period;
        set => _timer.Period = value;
    }

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
    private SemaphoreSlim _queryResSemaphore = new(1, 1);

    public HolePuncherClient(IPEndPoint serverEndPoint)
    {
        _serverEndPoint = serverEndPoint;
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

            var query = new QueryOp { Tags = tags };

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
                if (bytes.Length < 6)
                    continue;
                if (bytes[0] != 'K' || bytes[1] != 'U' || bytes[2] != 'H' || bytes[3] != 'P')
                    continue;

                var op = (EOperation)((bytes[4] << 8) | bytes[5]);
                switch (op)
                {
                    case EOperation.PunchRes:
                        {
                            PunchRes = Serializer.Deserialize<RemoteClient>(bytes[6..].AsMemory());
                            break;
                        }
                    case EOperation.QueryRes:
                        {
                            QueryRes? queryRes;
                            try
                            {
                                queryRes = Serializer.Deserialize<QueryRes>(bytes[6..].AsMemory());
                            }
                            catch
                            {
                                queryRes = null;
                            }

                            if (_queryResTask is not null && !_queryResTask.Task.IsCompleted)
                                _queryResTask.SetResult(queryRes);
                            break;
                        }
                }
            }
        }
        catch { }
    }

    public async Task Stop()
    {
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