namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using System;

public class HandshakeOp : ISerializable
{
    public required byte[] AesKey { get; init; }
    public required byte[] AesIV { get; init; }

    public int GetSerializedSize()
        => 1 + AesKey.Length + 1 + AesIV.Length;

    public void Serialize(Span<byte> span)
    {
        span[0] = (byte)AesKey.Length;
        AesKey.CopyTo(span[1..(1 + AesKey.Length)]);
        span[1 + AesKey.Length] = (byte)AesIV.Length;
        AesIV.CopyTo(span[(1 + AesKey.Length + 1)..(1 + AesKey.Length + 1 + AesIV.Length)]);
    }

    public static HandshakeOp Deserialize(ReadOnlySpan<byte> span)
        => new()
        {
            AesKey = span[1..(1 + span[0])].ToArray(),
            AesIV = span[(1 + span[0] + 1)..(1 + span[0] + 1 + span[1 + span[0]])].ToArray()
        };
}
