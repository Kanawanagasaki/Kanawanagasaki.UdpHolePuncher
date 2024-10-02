namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using ProtoBuf;

[ProtoContract]
public class HandshakeOp
{
    [ProtoMember(1)]
    public required byte[]? AesKey { get; init; }
    [ProtoMember(2)]
    public required byte[]? AesIV { get; init; }
}
