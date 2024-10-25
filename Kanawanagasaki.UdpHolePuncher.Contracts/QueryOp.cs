namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using System.Text;
using System.Xml.Linq;
using System;

public class QueryOp : ISerializable
{
    public required string Project { get; init; }
    public string[]? Tags { get; init; }
    public int Offset { get; init; } = 0;
    public EVisibility Visibility { get; init; } = EVisibility.Unset;

    public int GetSerializedSize()
        => 1                                                                                    // flags
        + 2 + Encoding.UTF32.GetByteCount(Project)                                              // project
        + (Tags is not null ? 2 + Tags.Length * 2 + Tags.Sum(Encoding.UTF8.GetByteCount) : 0)   // tags
        + 4                                                                                     // skip
        + 1;                                                                                    // visibility

    public void Serialize(Span<byte> span)
    {
        var hasTags = Tags is not null;
        var offset = 0;

        span[offset++] = (byte)(hasTags ? EPartFlag.HasTags : EPartFlag.None);

        var projectLen = Encoding.UTF8.GetByteCount(Project);
        span[offset++] = (byte)((projectLen >> 8) & 0xFF);
        span[offset++] = (byte)(projectLen & 0xFF);
        Encoding.UTF8.GetBytes(Project, span[offset..(offset + projectLen)]);
        offset += projectLen;

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

        span[offset++] = (byte)((Offset >> 24) & 0xFF);
        span[offset++] = (byte)((Offset >> 16) & 0xFF);
        span[offset++] = (byte)((Offset >> 8) & 0xFF);
        span[offset++] = (byte)(Offset & 0xFF);

        span[offset++] = (byte)Visibility;
    }

    public static QueryOp Deserialize(ReadOnlySpan<byte> span)
    {
        var offset = 0;

        var flags = (EPartFlag)span[offset++];

        var projectLen = (span[offset++] << 8) | span[offset++];
        var project = Encoding.UTF8.GetString(span[offset..(offset + projectLen)]);
        offset += projectLen;

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

        var clientsOffset = (span[offset++] << 24) | (span[offset++] << 16) | (span[offset++] << 8) | span[offset++];

        var visibility = (EVisibility)span[offset++];

        return new()
        {
            Project = project,
            Tags = tags,
            Offset = clientsOffset,
            Visibility = visibility
        };
    }

    public enum EVisibility : byte
    {
        Unset, Private, Public
    }
}
