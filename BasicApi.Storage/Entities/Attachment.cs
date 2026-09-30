namespace BasicApi.Storage.Entities;

/// <summary>A file in the object storage and what the server knows about it.</summary>
public class Attachment
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Kind { get; set; } = AttachmentKinds.File;
    public string FileName { get; set; } = string.Empty;
    public string Mime { get; set; } = string.Empty;
    public long Size { get; set; }
    public byte[]? Sha256 { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? DurationMs { get; set; }
    public byte[]? Waveform { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string? ThumbnailKey { get; set; }
    public string StorageState { get; set; } = StorageStates.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime? StoredAt { get; set; }
}

public static class AttachmentKinds
{
    public const string Photo = "photo";
    public const string Video = "video";
    public const string File = "file";
    public const string Voice = "voice";

    public static bool IsValid(string? kind) => kind is Photo or Video or File or Voice;
}

public static class StorageStates
{
    /// <summary>Waiting for the client to upload the file and confirm it.</summary>
    public const string Pending = "pending";

    /// <summary>Checked and kept in the storage.</summary>
    public const string Stored = "stored";

    /// <summary>The original was removed by the retention policy; the preview is kept.</summary>
    public const string Expired = "expired";
}
