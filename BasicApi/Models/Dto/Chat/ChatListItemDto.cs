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

    /// <summary>
    /// The photo to show for the chat: the group's, or the companion's in a private chat; null — none.
    /// Fetched by <c>POST /api/media/links</c>.
    /// </summary>
    public Guid? AvatarId { get; set; }

    public MessageDto? LastMessage { get; set; }
    public int UnreadCount { get; set; }

    /// <summary>Unread messages that mention the user.</summary>
    public int UnreadMentionCount { get; set; }

    /// <summary>
    /// Other members' reactions to the user's messages since the user last read the chat
    /// (<c>POST /api/chats/{chatId}/read</c>); show a mark on the row while it is above 0.
    /// </summary>
    public int UnreadReactionCount { get; set; }

    /// <summary>Seq of the last message the user has read.</summary>
    public long LastReadSeq { get; set; }

    /// <summary>The user marked the chat as unread; reading it clears the mark.</summary>
    public bool MarkedUnread { get; set; }

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

    /// <summary>Pinned to the top of the list: 1 — the topmost; null — not pinned.</summary>
    public int? PinnedPosition { get; set; }

    /// <summary>In the archive: not in the main list. A new message brings it back unless muted.</summary>
    public bool Archived { get; set; }

    /// <summary>Muted now: no notifications; the unread counter works as usual.</summary>
    public bool IsMuted { get; set; }

    /// <summary>Muted until then; null — not muted, or muted for good (<see cref="IsMuted"/>).</summary>
    public DateTime? MutedUntil { get; set; }

    /// <summary>The user's unsent message in this chat; null — none.</summary>
    public DraftDto? Draft { get; set; }
}
