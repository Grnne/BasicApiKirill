namespace BasicApi.Models.Dto.Message;

public class MessageDto
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Someone else's message — the caller has read it; one's own — another member has read it
    /// (<see cref="Status"/> is <c>read</c>). Filled in history and search; false in events.
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// For the caller's own messages: <c>sent</c>, <c>delivered</c> (a device of another member
    /// received it) or <c>read</c> (another member read it). Null for others' messages, in a chat
    /// with oneself and in events. Filled in history and search.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>Number of the message in the chat: 1, 2, 3, … Messages are ordered by it.</summary>
    public long Seq { get; set; }

    /// <summary>Id supplied by the sender when sending via REST; null — not supplied.</summary>
    public Guid? ClientMessageId { get; set; }

    /// <summary>Kind of message: <c>text</c> for now.</summary>
    public string Type { get; set; } = "text";

    /// <summary>When the text was last edited; null — never edited.</summary>
    public DateTime? EditedAt { get; set; }

    /// <summary>Formatting and mentions over <see cref="Text"/>; empty — plain text.</summary>
    public List<MessageEntityDto> Entities { get; set; } = [];

    /// <summary>Reactions, most popular first; empty — none.</summary>
    public List<ReactionCountDto> Reactions { get; set; } = [];

    /// <summary>
    /// The caller's own reaction. Filled in history and search; in events it is null —
    /// track your own from <c>ReactionsChanged</c>.
    /// </summary>
    public string? MyReaction { get; set; }

    /// <summary>The message this one answers; null — not a reply.</summary>
    public MessageReplyDto? ReplyTo { get; set; }

    /// <summary>For a forwarded message — its original author; null — not a forward.</summary>
    public MessageForwardDto? ForwardFrom { get; set; }
}

/// <summary>Preview of the answered message.</summary>
public class MessageReplyDto
{
    public Guid MessageId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;

    /// <summary>The start of the text; empty when the message was deleted.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The answered message was deleted for everyone.</summary>
    public bool Deleted { get; set; }
}

public class MessageForwardDto
{
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
}
