﻿namespace Kanawanagasaki.UdpHolePuncher.Contracts;

using System.Text;
using System;

public class PunchOp : ISerializable
{
    public required bool IsQuerable { get; set; }
    public required string Project { get; init; }
    public string? Name { get; init; }
    public string? Password { get; init; }
    public byte[]? Extra { get; init; }
    public string[]? Tags { get; init; }

    public int GetSerializedSize()
        => 1                                                                                    // flags
        + 2 + Encoding.UTF32.GetByteCount(Project)                                              // project
        + (Name is not null ? 2 + Encoding.UTF8.GetByteCount(Name) : 0)                         // name
        + (Password is not null ? 2 + Encoding.UTF8.GetByteCount(Password) : 0)                 // password
        + (Extra is not null ? 2 + Extra.Length : 0)                                            // extra
        + (Tags is not null ? 2 + Tags.Length * 2 + Tags.Sum(Encoding.UTF8.GetByteCount) : 0);  // tags;

    public void Serialize(Span<byte> span)
    {
        var hasName = Name is not null;
        var hasPassword = Password is not null;
        var hasExtra = Extra is not null;
        var hasTags = Tags is not null;
        
#pragma warning disable format
        var flags = (hasName     ? 0b01000000 : 0)
                  | (hasPassword ? 0b00100000 : 0)
                  | (hasExtra    ? 0b00010000 : 0)
                  | (hasTags     ? 0b00001000 : 0)
                  | (IsQuerable  ? 0b00000100 : 0);
#pragma warning restore format

        span[0] = (byte)flags;

        var offset = 1;

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

    public static PunchOp Deserialize(ReadOnlySpan<byte> span)
    {
#pragma warning disable format
        var hasName =     (span[0] & 0b01000000) != 0;
        var hasPassword = (span[0] & 0b00100000) != 0;
        var hasExtra =    (span[0] & 0b00010000) != 0;
        var hasTags =     (span[0] & 0b00001000) != 0;
        var isQuerable =  (span[0] & 0b00000100) != 0;
#pragma warning restore format

        var offset = 1;

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

        string? password = null;
        if (hasPassword)
        {
            var passwordLen = (span[offset++] << 8) | span[offset++];
            password = Encoding.UTF8.GetString(span[offset..(offset + passwordLen)]);
            offset += passwordLen;
        }

        byte[]? extra = null;
        if (hasExtra)
        {
            var extraLen = (span[offset++] << 8) | span[offset++];
            extra = span[offset..(offset + extraLen)].ToArray();
            offset += extraLen;
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
            IsQuerable = isQuerable,
            Project = project,
            Name = name,
            Password = password,
            Extra = extra,
            Tags = tags
        };
    }
}
