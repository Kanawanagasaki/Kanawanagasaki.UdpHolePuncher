namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using System.Text;
using System.Xml.Linq;
using System;

public class QueryOp : ISerializable
{
    public required string Project { get; init; }
    public string[]? Tags { get; init; }

    public int GetSerializedSize()
    => 1                                                                                    // flags
    + 2 + Encoding.UTF32.GetByteCount(Project)                                              // project
    + (Tags is not null ? 2 + Tags.Length * 2 + Tags.Sum(Encoding.UTF8.GetByteCount) : 0);  // tags;

    public void Serialize(Span<byte> span)
    {
        var hasTags = Tags is not null;

        span[0] = (byte)(hasTags ? 0b00001000 : 0);

        var offset = 1;

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
    }

    public static QueryOp Deserialize(ReadOnlySpan<byte> span)
    {
        var hasTags = (span[0] & 0b00001000) != 0;

        var offset = 1;

        var projectLen = (span[offset++] << 8) | span[offset++];
        var project = Encoding.UTF8.GetString(span[offset..(offset + projectLen)]);
        offset += projectLen;

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
            Project = project,
            Tags = tags
        };
    }
}
