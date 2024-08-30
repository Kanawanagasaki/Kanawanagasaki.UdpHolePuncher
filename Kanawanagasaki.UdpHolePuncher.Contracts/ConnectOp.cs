namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using ProtoBuf;

[ProtoContract]
public class ConnectOp
{
    [ProtoMember(1)]
    public required RemoteClient RemoteClient { get; init; }
}
