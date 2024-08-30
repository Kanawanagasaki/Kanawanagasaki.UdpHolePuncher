namespace Kanawanagasaki.UdpHolePuncher.Contracts;

public enum EOperation : ushort
{
    Punch,
    PunchRes,
    Query,
    QueryRes,
    Connect,
    Disconnect,
    Ping,
    Pong,
    Data
}
