namespace BasicApi.Models.Dto.Chat;

/// <summary>
/// <c>ReadStateChanged</c>: the chat's counters changed for the user — they read it on one of
/// their devices, or a reaction to their message came or went. Devices take the counters and the
/// mark from it as they are.
/// </summary>
public class ReadStateDto
{
    public Guid ChatId { get; set; }
    public long LastReadSeq { get; set; }
    public int UnreadCount { get; set; }
    public int UnreadMentionCount { get; set; }

    /// <summary>As <c>ChatListItemDto.unreadReactionCount</c>.</summary>
    public int UnreadReactionCount { get; set; }
    public bool MarkedUnread { get; set; }
}
