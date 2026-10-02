using BasicApi.Features.Groups;
using BasicApi.Features.Media;
using BasicApi.Features.Push;
using BasicApi.Models;
using BasicApi.Services;

namespace BasicApi.Features.Config;

/// <summary>The instance's limits and switches a client needs before the server rejects a request.</summary>
public class ClientConfigDto
{
    public MessageLimitsDto Messages { get; set; } = new();
    public MediaLimitsDto Media { get; set; } = new();
    public GroupLimitsDto Groups { get; set; } = new();
    public PushSwitchDto Push { get; set; } = new();

    public static ClientConfigDto From(
        MessageOptions messages, StorageOptions storage, MediaOptions media, GroupOptions groups, PushOptions push) => new()
    {
        Messages = new()
        {
            MaxLength = MessageText.MaxLength,
            MaxAttachments = MessageAttachments.MaxPerMessage,
            Reactions = [.. messages.AllowedReactions],
            EditWindowHours = messages.EditWindowHours,
            DeleteWindowHours = messages.DeleteWindowHours
        },
        Media = new()
        {
            Enabled = storage.IsConfigured,
            MaxFileSize = media.MaxFileSize,
            MaxPhotoSize = media.MaxPhotoSize,
            MaxPngGifPhotoPixels = ImagePreviews.MaxDecodedPixels,
            RetentionDays = media.RetentionDays
        },
        Groups = new() { MaxMembers = groups.MaxMembers, MaxTitleLength = GroupOptions.MaxTitleLength },
        Push = new() { Enabled = push.IsConfigured }
    };
}

public class MessageLimitsDto
{
    /// <summary>Longest text of a message, caption or draft, in UTF-16 code units.</summary>
    public int MaxLength { get; set; }

    /// <summary>Most files in one message (an album).</summary>
    public int MaxAttachments { get; set; }

    /// <summary>The reactions users may put, in the order to show them.</summary>
    public List<string> Reactions { get; set; } = [];

    /// <summary>How long the author may edit a message, in hours; 0 — no limit.</summary>
    public int EditWindowHours { get; set; }

    /// <summary>How long the author may delete a message for everyone, in hours; 0 — no limit.</summary>
    public int DeleteWindowHours { get; set; }
}

public class MediaLimitsDto
{
    /// <summary>false — file storage is not set up: media endpoints answer 503 <c>MEDIA_UNAVAILABLE</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Largest file, in bytes.</summary>
    public long MaxFileSize { get; set; }

    /// <summary>Largest photo (sent with kind <c>photo</c>), in bytes: the server decodes it for the preview.</summary>
    public long MaxPhotoSize { get; set; }

    /// <summary>
    /// Most pixels (width × height) of a PNG or GIF photo: the server decodes these at full size
    /// for the preview, so a larger one is refused with <c>INVALID_MEDIA</c> — send it as a file.
    /// </summary>
    public long MaxPngGifPhotoPixels { get; set; }

    /// <summary>How many days files are kept; 0 — forever.</summary>
    public int RetentionDays { get; set; }
}

public class GroupLimitsDto
{
    /// <summary>Most members of a group, the owner included.</summary>
    public int MaxMembers { get; set; }

    public int MaxTitleLength { get; set; }
}

public class PushSwitchDto
{
    /// <summary>Whether the server sends push notifications; the key is in <c>GET /api/push/config</c>.</summary>
    public bool Enabled { get; set; }
}
