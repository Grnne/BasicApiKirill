namespace BasicApi.Models.Dto.Message;

/// <summary>
/// What a push notification carries to the service worker's <c>push</c> event: enough to show it
/// without asking the API, since the worker has no access token.
/// </summary>
public class PushNotificationDto
{
    public const string MessageKind = "message";
    public const string ReactionKind = "reaction";

    /// <summary>
    /// <c>message</c> — a new message; <c>reaction</c> — a reaction to the recipient's message
    /// (<c>emoji</c>; the sender is who reacted, the message fields are the message's). More kinds may
    /// come: ignore an unknown one.
    /// </summary>
    public string Kind { get; set; } = MessageKind;

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

    /// <summary>The reaction (<c>kind: reaction</c>); null for a message.</summary>
    public string? Emoji { get; set; }

    /// <summary>When the message was sent, or the reaction made.</summary>
    public DateTime CreatedAt { get; set; }
}
