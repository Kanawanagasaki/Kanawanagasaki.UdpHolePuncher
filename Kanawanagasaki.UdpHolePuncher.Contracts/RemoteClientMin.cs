namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using System.Text;

public class RemoteClientMin : ISerializable
{
    public Guid Uuid { get; init; } = Guid.NewGuid();
    public required string Project { get; init; }
    public string? Name { get; set; }
    public string[]? Tags { get; set; }

    public override string ToString()
        => $"PublicRemoteClient\n\tProject: {Project}{(Name is null ? "" : $"\n\tName: {Name}")}\n\tTags: [{string.Join(", ", Tags ?? [])}]";

    public int GetSerializedSize()
        => 1                                                                                    // flags
        + 16                                                                                    // uuid
        + 2 + Encoding.UTF32.GetByteCount(Project)                                              // project
        + (Name is not null ? 2 + Encoding.UTF8.GetByteCount(Name) : 0)                         // name
        + (Tags is not null ? 2 + Tags.Length * 2 + Tags.Sum(Encoding.UTF8.GetByteCount) : 0);  // tags;

    public void Serialize(Span<byte> span)
    {
        var hasName = Name is not null;
        var hasTags = Tags is not null;
        
#pragma warning disable format
        var flags = (hasName     ? 0b01000000 : 0)
                  | (hasTags     ? 0b00001000 : 0);
#pragma warning restore format

        span[0] = (byte)flags;

        var offset = 1;

        Uuid.TryWriteBytes(span[offset..(offset + 16)]);
        offset += 16;

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

    public static RemoteClientMin Deserialize(ReadOnlySpan<byte> span)
    {
#pragma warning disable format
        var hasName =     (span[0] & 0b01000000) != 0;
        var hasTags =     (span[0] & 0b00001000) != 0;
#pragma warning restore format

        var offset = 1;

        var uuid = span[offset..(offset + 16)];
        offset += uuid.Length;

        var projectLen = (span[offset++] << 8) | span[offset++];
        var project = Encoding.UTF8.GetString(span[offset..(offset + projectLen)]);
        offset += projectLen;

        string? name = null;
        if (hasName)
        {
            var nameLen = (span[offset++] << 8) | span[offset++];
            name = Encoding.UTF8.GetString(span[offset..(offset + nameLen)]);
            offset += nameLen;
        }

        string[]? tags = null;
        if (hasTags)
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
            Project = project,
            Name = name,
            Tags = tags
        };
    }
}
