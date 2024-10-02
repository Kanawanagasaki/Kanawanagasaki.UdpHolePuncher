namespace Kanawanagasaki.UdpHolePuncher.Server;

using Kanawanagasaki.UdpHolePuncher.Contracts;
using System.Net;
using System.Security.Cryptography;

internal class Client : IDisposable
{
    internal IPEndPoint Endpoint { get; }
    internal long LastTimeSeen { get; set; } = 0;

    internal RemoteClient? RemoteClient { get; set; }

    internal AesGcm? Aes { get; set; }
    internal byte[]? AesKey { get; set; }
    internal byte[]? AesIV { get; set; }

    internal Client(IPEndPoint endpoint)
    {
        Endpoint = endpoint;
    }

    public void Dispose()
    {
        Aes?.Dispose();
    }
}
