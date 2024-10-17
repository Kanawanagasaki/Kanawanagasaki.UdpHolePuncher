namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using System;

public class QueryRes : ISerializable
{
    public required RemoteClientMin[] PrivateClients { get; init; }
    public required RemoteClient[] PublicClients { get; init; }

    public int GetSerializedSize()
        => 2
        + PrivateClients.Sum(x => 2 + x.GetSerializedSize())
        + 2
        + PublicClients.Sum(x => 2 + x.GetSerializedSize());

    public void Serialize(Span<byte> span)
    {
        int offset = 0;

        span[offset++] = (byte)((PrivateClients.Length >> 8) & 0xFF);
        span[offset++] = (byte)(PrivateClients.Length & 0xFF);

        foreach (var client in PrivateClients)
        {
            var size = client.GetSerializedSize();

            span[offset++] = (byte)((size >> 8) & 0xFF);
            span[offset++] = (byte)(size & 0xFF);

            client.Serialize(span[offset..(offset + size)]);
            offset += size;
        }

        span[offset++] = (byte)((PublicClients.Length >> 8) & 0xFF);
        span[offset++] = (byte)(PublicClients.Length & 0xFF);

        foreach (var client in PublicClients)
        {
            var size = client.GetSerializedSize();

            span[offset++] = (byte)((size >> 8) & 0xFF);
            span[offset++] = (byte)(size & 0xFF);

            client.Serialize(span[offset..(offset + size)]);
            offset += size;
        }
    }

    public static QueryRes Deserialize(ReadOnlySpan<byte> span)
    {
        int offset = 0;

        var privateClientsLen = (span[offset++] << 8) | span[offset++];
        var privateClients = new RemoteClientMin[privateClientsLen];
        for (int i = 0; i < privateClientsLen; i++)
        {
            var clientLen = (span[offset++] << 8) | span[offset++];
            privateClients[i] = RemoteClientMin.Deserialize(span[offset..(offset + clientLen)]);
            offset += clientLen;
        }

        var publicClientsLen = (span[offset++] << 8) | span[offset++];
        var publicClients = new RemoteClient[publicClientsLen];
        for (int i = 0; i < publicClientsLen; i++)
        {
            var clientLen = (span[offset++] << 8) | span[offset++];
            publicClients[i] = RemoteClient.Deserialize(span[offset..(offset + clientLen)]);
            offset += clientLen;
        }

        return new()
        {
            PrivateClients = privateClients,
            PublicClients = publicClients
        };
    }
}
