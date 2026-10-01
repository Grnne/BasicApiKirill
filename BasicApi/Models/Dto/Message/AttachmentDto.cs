namespace BasicApi.Models.Dto.Message;

/// <summary>A file as clients see it, in messages and on avatars. The bytes are fetched by link.</summary>
public class AttachmentDto
{
    public Guid Id { get; set; }

    /// <summary><c>photo</c>, <c>video</c>, <c>file</c> or <c>voice</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>The type the server found in the file; <c>application/octet-stream</c> — unknown.</summary>
    public string MimeType { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>Photo: as shown (camera rotation applied); video: as the client said.</summary>
    public int? Width { get; set; }
    public int? Height { get; set; }

    public int? DurationMs { get; set; }
    public List<int>? Waveform { get; set; }

    /// <summary>There is a preview (photo; video when the client sent a frame).</summary>
    public bool HasThumbnail { get; set; }

    /// <summary>
    /// <c>stored</c>; <c>expired</c> — the original was removed by the server's retention policy,
    /// only the preview is left.
    /// </summary>
    public string State { get; set; } = string.Empty;
}
