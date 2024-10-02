namespace Kanawanagasaki.UdpHolePuncher.Contracts;

public enum EOperation : ushort
{
    Punch,
    PunchRes,
    Query,
    QueryRes,
    Connect,
    P2P,
    Handshake,
    HandshakeAck,
    Data
}
