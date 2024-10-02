namespace Kanawanagasaki.UdpHolePuncher.Client;

public enum EConnectionStatus : byte
{
    Handshake = 1,
    Connected = 2,
    Disconnected = 127
}
