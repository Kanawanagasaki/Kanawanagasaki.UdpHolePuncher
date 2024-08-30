namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using ProtoBuf;

[ProtoContract]
public class QueryOp
{
    [ProtoMember(1)]
    public required string[]? Tags { get; init; }
}
