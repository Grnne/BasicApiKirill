using BasicApi.Models.Dto.Message;

namespace BasicApi.Models.Dto.Chat;

public class ChatListItemDto
{
    public Guid ChatId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Guid? CompanionId { get; set; }
    public string? CompanionName { get; set; }

    /// <summary>Companion username (private chats only, null for groups).</summary>
    public string? CompanionUsername { get; set; }

    public MessageDto? LastMessage { get; set; }
    public int UnreadCount { get; set; }

    /// <summary>Unread messages that mention the user.</summary>
    public int UnreadMentionCount { get; set; }

    /// <summary>Seq of the last message the user has read.</summary>
    public long LastReadSeq { get; set; }

    /// <summary>
    /// The user's own messages up to this seq have been read by another member; 0 — none.
    /// Grows with <c>MessagesRead</c>.
    /// </summary>
    public long OutboxReadSeq { get; set; }

    /// <summary>
    /// The user's own messages up to this seq have reached another member's device; 0 — none.
    /// Grows with <c>MessagesDelivered</c> and <c>MessagesRead</c>.
    /// </summary>
    public long OutboxDeliveredSeq { get; set; }

    public DateTime LastActivityAt { get; set; }
}
