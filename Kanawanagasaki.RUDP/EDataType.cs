namespace Kanawanagasaki.RUDP;

[Flags]
internal enum EDataType : byte
{
    Datagram = 0b1,
    ReliableDatagram = 0b10,
    ReliableDatagramAck = 0b100,
    Stream = 0b1000
}
