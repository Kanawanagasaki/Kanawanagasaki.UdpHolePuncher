namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using ProtoBuf;
using System.Net;

[ProtoContract]
public class RemoteClient
{
    [ProtoMember(1)]
    public required byte[]? IpBytes { get; init; }
    [ProtoMember(2)]
    public required int Port { get; init; }
    [ProtoMember(3)]
    public string? Name { get; set; }
    [ProtoMember(4)]
    public byte[]? Extra { get; set; }
    [ProtoMember(5)]
    public required string[]? Tags { get; set; }
    [ProtoMember(6)]
    public required DateTime LastPunch { get; set; }

    public IPAddress Ip => new IPAddress(IpBytes ?? []);
    public IPEndPoint EndPoint => new IPEndPoint(Ip, Port);

    public override string ToString()
        => $"RemoteClient {string.Join(".", IpBytes ?? [])}:{Port}{(Name is null ? "" : $"\n\tName: {Name}")}\n\tTags: [{string.Join(", ", Tags ?? [])}]\n\tLast Punch: {LastPunch:O}";
}
