namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using System;

public class P2POp : ISerializable
{
    public required RemoteClient RemoteClient { get; init; }
    public required byte[] AesKey { get; init; }
    public required byte[] AesIV { get; init; }

    public int GetSerializedSize()
        => 2 + RemoteClient.GetSerializedSize() + 1 + AesKey.Length + 1 + AesIV.Length;

    public void Serialize(Span<byte> span)
    {
        int offset = 0;

        var clientLen = RemoteClient.GetSerializedSize();
        span[offset++] = (byte)(clientLen >> 8);
        span[offset++] = (byte)(clientLen & 0xFF);
        RemoteClient.Serialize(span[offset..(offset + clientLen)]);
        offset += clientLen;

        span[offset++] = (byte)AesKey.Length;
        AesKey.CopyTo(span[offset..(offset + AesKey.Length)]);
        offset += AesKey.Length;

        span[offset++] = (byte)AesIV.Length;
        AesIV.CopyTo(span[offset..(offset + AesIV.Length)]);
    }

    public static P2POp Deserialize(ReadOnlySpan<byte> span)
    {
        int offset = 0;

        var clientLen = (span[offset++] << 8) | span[offset++];
        var client = RemoteClient.Deserialize(span[offset..(offset + clientLen)]);
        offset += clientLen;

        var aesKeyLen = span[offset++];
        var aesKey = span[offset..(offset + aesKeyLen)];
        offset += aesKeyLen;

        var aeIvLen = span[offset++];
        var aeIv = span[offset..(offset + aeIvLen)];

        return new()
        {
            RemoteClient = client,
            AesKey = aesKey.ToArray(),
            AesIV = aeIv.ToArray()
        };
    }
}
