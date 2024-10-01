﻿namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using ProtoBuf;
using System.Net;

[ProtoContract]
public class ConnectOp
{
    [ProtoMember(1)]
    public required byte[]? IpBytes { get; init; }
    [ProtoMember(2)]
    public required int Port { get; init; }
    [ProtoMember(3)]
    public string? Token { get; set; }

    public IPAddress Ip => new IPAddress(IpBytes ?? []);
    public IPEndPoint EndPoint => new IPEndPoint(Ip, Port);
}