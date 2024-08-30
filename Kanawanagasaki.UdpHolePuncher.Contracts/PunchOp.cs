namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using ProtoBuf;

[ProtoContract]
public class PunchOp
{
    [ProtoMember(1)]
    public required string[]? Tags { get; init; }
    [ProtoMember(2)]
    public string? Name { get; set; }
    [ProtoMember(3)]
    public byte[]? Extra { get; set; }
}
