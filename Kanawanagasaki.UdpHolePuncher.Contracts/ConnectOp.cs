namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using System;
using System.Net;
using System.Text;

public class ConnectOp : ISerializable
{
    public required Guid Uuid { get; init; }
    public string? Password { get; init; }

    public int GetSerializedSize()
        => 1 + 16 + (Password is null ? 0 : 2 + Encoding.UTF8.GetByteCount(Password));

    public void Serialize(Span<byte> span)
    {
        var offset = 0;

        span[offset++] = (byte)(Password is not null ? EPartFlag.HasPassword : EPartFlag.None);

        Uuid.TryWriteBytes(span[offset..(offset + 16)]);
        offset += 16;

        if (Password is not null)
        {
            var passwordLen = Encoding.UTF8.GetByteCount(Password);
            span[offset++] = (byte)((passwordLen >> 8) & 0xFF);
            span[offset++] = (byte)(passwordLen & 0xFF);
            Encoding.UTF8.GetBytes(Password, span[offset..(offset + passwordLen)]);
            offset += passwordLen;
        }
    }

    public static ConnectOp Deserialize(ReadOnlySpan<byte> span)
    {
        var offset = 0;

        var flags = (EPartFlag)span[offset++];

        var uuid = span[offset..(offset + 16)];
        offset += uuid.Length;

        string? password = null;
        if (flags.HasFlag(EPartFlag.HasPassword))
        {
            var passwordLen = (span[offset++] << 8) | span[offset++];
            password = Encoding.UTF8.GetString(span[offset..(offset + passwordLen)]);
            offset += passwordLen;
        }

        return new()
        {
            Uuid = new Guid(uuid),
            Password = password,
        };
    }
}
