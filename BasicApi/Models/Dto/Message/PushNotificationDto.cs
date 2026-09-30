namespace BasicApi.Models.Dto.Message;

/// <summary>
/// What a push notification carries — the browser decrypts it and hands it to the service worker's
/// <c>push</c> event (<c>event.data.json()</c>). Enough to show the notification without asking
/// the API: the worker has no access token.
/// </summary>
public class PushNotificationDto
{
    /// <summary><c>message</c> — a new message. More kinds may come: ignore an unknown one.</summary>
    public string Kind { get; set; } = "message";

    public Guid ChatId { get; set; }

    /// <summary><c>private</c> or <c>group</c>.</summary>
    public string ChatType { get; set; } = string.Empty;

    /// <summary>The group's title; null in a private chat — show the sender's name.</summary>
    public string? ChatTitle { get; set; }

    public Guid MessageId { get; set; }
    public long Seq { get; set; }
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;

    /// <summary>As <c>MessageDto.type</c>: <c>text</c>, <c>media</c>, <c>system</c>.</summary>
    public string MessageType { get; set; } = "text";

    /// <summary>The start of the text (up to 100 characters); for media — of the caption, may be empty.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Media: the kind of the first file (<c>photo</c>, <c>video</c>, <c>file</c>, <c>voice</c>); null otherwise.</summary>
    public string? AttachmentKind { get; set; }

    public int AttachmentCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
