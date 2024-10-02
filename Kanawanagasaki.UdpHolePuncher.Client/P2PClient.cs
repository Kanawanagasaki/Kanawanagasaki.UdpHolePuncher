namespace Kanawanagasaki.UdpHolePuncher.Client;

using Kanawanagasaki.UdpHolePuncher.Contracts;
using System.Net;
using System.Security.Cryptography;

internal class P2PClient
{
    internal RemoteClient RemoteClient { get; }
    internal IPEndPoint EndPoint => RemoteClient.EndPoint;

    internal AesGcm Aes { get; }
    internal byte[] AesIV { get; }

    internal EConnectionStatus ConnectionStatus { get; set; } = EConnectionStatus.Disconnected;

    internal P2PClient(RemoteClient remoteClient, byte[] aesKey, byte[] aesIV)
    {
        RemoteClient = remoteClient;
        Aes = new AesGcm(aesKey, 16);
        AesIV = aesIV;
    }
}
