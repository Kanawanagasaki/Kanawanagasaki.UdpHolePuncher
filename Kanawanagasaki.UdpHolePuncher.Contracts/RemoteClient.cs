namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using System.Net;
using System.Text;

public class RemoteClient : ISerializable
{
    public Guid Uuid { get; init; } = Guid.NewGuid();
    public required byte[] IpBytes { get; init; }
    public required ushort Port { get; init; }
    public required string Project { get; set; }
    public string? Name { get; set; }
    public string? Password { get; set; }
    public byte[]? Extra { get; set; }
    public byte[]? PublicExtra { get; set; }
    public string[]? Tags { get; set; }

    public IPAddress Ip => new IPAddress(IpBytes);
    public IPEndPoint EndPoint => new IPEndPoint(Ip, Port);

    public RemoteClientMin ToMin()
        => new()
        {
            Uuid = Uuid,
            Project = Project,
            Name = Name,
            PublicExtra = PublicExtra,
            Tags = Tags
        };

    public override string ToString()
        => $"RemoteClient {string.Join(".", IpBytes ?? [])}:{Port}\n\tProject: {Project}{(Name is null ? "" : $"\n\tName: {Name}")}\n\tTags: [{string.Join(", ", Tags ?? [])}]";

    public int GetSerializedSize()
        => 1                                                                                    // flags
        + 16                                                                                    // uuid
        + IpBytes.Length                                                                        // ip
        + 2                                                                                     // port
        + 2 + Encoding.UTF32.GetByteCount(Project)                                              // project
        + (Name is not null ? 2 + Encoding.UTF8.GetByteCount(Name) : 0)                         // name
        + (Password is not null ? 2 + Encoding.UTF8.GetByteCount(Password) : 0)                 // password
        + (Extra is not null ? 2 + Extra.Length : 0)                                            // extra
        + (PublicExtra is not null ? 2 + PublicExtra.Length : 0)                                // public extra
        + (Tags is not null ? 2 + Tags.Length * 2 + Tags.Sum(Encoding.UTF8.GetByteCount) : 0);  // tags

    public void Serialize(Span<byte> span)
    {
        var isIpV6 = IpBytes.Length == 16;
        var hasName = Name is not null;
        var hasPassword = Password is not null;
        var hasExtra = Extra is not null;
        var hasPublicExtra = PublicExtra is not null;
        var hasTags = Tags is not null;

        var flags = (isIpV6 ? EPartFlag.IsIpV6 : EPartFlag.None)
                  | (hasName ? EPartFlag.HasName : EPartFlag.None)
                  | (hasPassword ? EPartFlag.HasPassword : EPartFlag.None)
                  | (hasExtra ? EPartFlag.HasExtra : EPartFlag.None)
                  | (hasPublicExtra ? EPartFlag.HasPublicExtra : EPartFlag.None)
                  | (hasTags ? EPartFlag.HasTags : EPartFlag.None);

        span[0] = (byte)flags;

        var offset = 1;

        Uuid.TryWriteBytes(span[offset..(offset + 16)]);
        offset += 16;

        IpBytes.CopyTo(span[offset..(offset + IpBytes.Length)]);
        offset += IpBytes.Length;

        span[offset++] = (byte)(Port >> 8);
        span[offset++] = (byte)(Port & 0xFF);

        var projectLen = Encoding.UTF8.GetByteCount(Project);
        span[offset++] = (byte)((projectLen >> 8) & 0xFF);
        span[offset++] = (byte)(projectLen & 0xFF);
        Encoding.UTF8.GetBytes(Project, span[offset..(offset + projectLen)]);
        offset += projectLen;

        if (Name is not null)
        {
            var nametLen = Encoding.UTF8.GetByteCount(Name);
            span[offset++] = (byte)((nametLen >> 8) & 0xFF);
            span[offset++] = (byte)(nametLen & 0xFF);
            Encoding.UTF8.GetBytes(Name, span[offset..(offset + nametLen)]);
            offset += nametLen;
        }

        if (Password is not null)
        {
            var passwordLen = Encoding.UTF8.GetByteCount(Password);
            span[offset++] = (byte)((passwordLen >> 8) & 0xFF);
            span[offset++] = (byte)(passwordLen & 0xFF);
            Encoding.UTF8.GetBytes(Password, span[offset..(offset + passwordLen)]);
            offset += passwordLen;
        }

        if (Extra is not null)
        {
            span[offset++] = (byte)((Extra.Length >> 8) & 0xFF);
            span[offset++] = (byte)(Extra.Length & 0xFF);
            Extra.CopyTo(span[offset..(offset + Extra.Length)]);
            offset += Extra.Length;
        }

        if (PublicExtra is not null)
        {
            span[offset++] = (byte)((PublicExtra.Length >> 8) & 0xFF);
            span[offset++] = (byte)(PublicExtra.Length & 0xFF);
            PublicExtra.CopyTo(span[offset..(offset + PublicExtra.Length)]);
            offset += PublicExtra.Length;
        }

        if (Tags is not null)
        {
            span[offset++] = (byte)((Tags.Length >> 8) & 0xFF);
            span[offset++] = (byte)(Tags.Length & 0xFF);
            foreach (var tag in Tags)
            {
                var tagLen = Encoding.UTF8.GetByteCount(tag);
                span[offset++] = (byte)((tagLen >> 8) & 0xFF);
                span[offset++] = (byte)(tagLen & 0xFF);
                Encoding.UTF8.GetBytes(tag, span[offset..(offset + tagLen)]);
                offset += tagLen;
            }
        }
    }

    public static RemoteClient Deserialize(ReadOnlySpan<byte> span)
    {
        var offset = 0;

        var flags = (EPartFlag)span[offset++];

        var uuid = span[offset..(offset + 16)];
        offset += uuid.Length;

        var ip = flags.HasFlag(EPartFlag.IsIpV6) ? span[offset..(offset + 16)] : span[offset..(offset + 4)];
        offset += ip.Length;

        var port = (span[offset++] << 8) | span[offset++];

        var projectLen = (span[offset++] << 8) | span[offset++];
        var project = Encoding.UTF8.GetString(span[offset..(offset + projectLen)]);
        offset += projectLen;

        string? name = null;
        if (flags.HasFlag(EPartFlag.HasName))
        {
            var nameLen = (span[offset++] << 8) | span[offset++];
            name = Encoding.UTF8.GetString(span[offset..(offset + nameLen)]);
            offset += nameLen;
        }

        string? password = null;
        if (flags.HasFlag(EPartFlag.HasPassword))
        {
            var passwordLen = (span[offset++] << 8) | span[offset++];
            password = Encoding.UTF8.GetString(span[offset..(offset + passwordLen)]);
            offset += passwordLen;
        }

        byte[]? extra = null;
        if (flags.HasFlag(EPartFlag.HasExtra))
        {
            var extraLen = (span[offset++] << 8) | span[offset++];
            extra = span[offset..(offset + extraLen)].ToArray();
            offset += extraLen;
        }

        byte[]? publicExtra = null;
        if (flags.HasFlag(EPartFlag.HasPublicExtra))
        {
            var publicExtraLen = (span[offset++] << 8) | span[offset++];
            publicExtra = span[offset..(offset + publicExtraLen)].ToArray();
            offset += publicExtraLen;
        }

        string[]? tags = null;
        if (flags.HasFlag(EPartFlag.HasTags))
        {
            var tagsNumber = (span[offset++] << 8) | span[offset++];
            tags = new string[tagsNumber];
            for (int i = 0; i < tagsNumber; i++)
            {
                var tagLen = (span[offset++] << 8) | span[offset++];
                tags[i] = Encoding.UTF8.GetString(span[offset..(offset + tagLen)]);
                offset += tagLen;
            }
        }

        return new()
        {
            Uuid = new Guid(uuid),
            IpBytes = ip.ToArray(),
            Port = (ushort)port,
            Project = project,
            Name = name,
            Password = password,
            Extra = extra,
            PublicExtra = publicExtra,
            Tags = tags
        };
    }
}
