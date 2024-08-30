namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using ProtoBuf;

[ProtoContract]
public class QueryRes
{
    [ProtoMember(1)]
    public required RemoteClient[]? Clients { get; init; }
}
