using System.Text.Json;
using BasicApi.Models.Dto.Message;
using BasicApi.Storage.Entities;

namespace BasicApi.Services;

/// <summary>The files of a message as the queries return them (JSON), turned into DTOs.</summary>
public static class MessageAttachments
{
    /// <summary>How many files one message (an album) may carry.</summary>
    public const int MaxPerMessage = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static List<AttachmentDto> Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return [];
        return [.. JsonSerializer.Deserialize<List<Row>>(json, Json)!.Select(r => new AttachmentDto
        {
            Id = r.Id,
            Kind = r.Kind,
            FileName = r.FileName,
            MimeType = r.MimeType,
            Size = r.Size,
            Width = r.Width,
            Height = r.Height,
            DurationMs = r.DurationMs,
            Waveform = r.Waveform is null ? null : [.. Convert.FromBase64String(r.Waveform).Select(b => (int)b)],
            HasThumbnail = r.HasThumbnail,
            State = r.State
        })];
    }

    public static AttachmentDto ToDto(Attachment a) => new()
    {
        Id = a.Id,
        Kind = a.Kind,
        FileName = a.FileName,
        MimeType = a.Mime,
        Size = a.Size,
        Width = a.Width,
        Height = a.Height,
        DurationMs = a.DurationMs,
        Waveform = a.Waveform?.Select(b => (int)b).ToList(),
        HasThumbnail = a.ThumbnailKey is not null,
        State = a.StorageState
    };

    private sealed record Row(
        Guid Id, string Kind, string FileName, string MimeType, long Size, int? Width, int? Height,
        int? DurationMs, string? Waveform, bool HasThumbnail, string State);
}
