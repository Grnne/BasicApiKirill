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

    /// <summary>
    /// Kind of message: <c>text</c>, <c>media</c> (files in <see cref="Attachments"/>, the text is the
    /// caption and may be empty) or <c>system</c> (see <see cref="Action"/>); more kinds will come —
    /// show an unknown one as a stub.
    /// </summary>
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

    /// <summary>The files, in album order; empty — none. The bytes are fetched by <c>POST /api/media/links</c>.</summary>
    public List<AttachmentDto> Attachments { get; set; } = [];

    /// <summary>
    /// For a system message (<see cref="Type"/> <c>system</c>) — what happened; the sender is who did
    /// it, and <see cref="Text"/> says the same in words for clients that do not know the action.
    /// Null for other messages.
    /// </summary>
    public MessageActionDto? Action { get; set; }
}

/// <summary>What a system message records.</summary>
public class MessageActionDto
{
    /// <summary><c>group_created</c>, <c>title_changed</c>, <c>members_added</c>, <c>member_removed</c>, <c>member_left</c>, <c>photo_changed</c>, <c>photo_removed</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Whom it concerns: the added or removed members, the one who left.</summary>
    public List<Guid> UserIds { get; set; } = [];

    /// <summary>The group's title — for <c>group_created</c> and <c>title_changed</c>.</summary>
    public string? Title { get; set; }
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
