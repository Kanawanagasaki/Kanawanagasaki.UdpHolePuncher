namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using ProtoBuf;

[ProtoContract]
public class P2POp
{
    [ProtoMember(1)]
    public required RemoteClient? RemoteClient { get; init; }
    [ProtoMember(2)]
    public required byte[]? AesKey { get; init; }
    [ProtoMember(3)]
    public required byte[]? AesIV { get; init; }
}
