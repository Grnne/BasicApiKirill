namespace BasicApi.Models.Dto.Chat;

/// <summary>
/// <c>ReadStateChanged</c>: the user's reading of a chat changed on one of their devices —
/// the others update the counters and the mark from it.
/// </summary>
public class ReadStateDto
{
    public Guid ChatId { get; set; }
    public long LastReadSeq { get; set; }
    public int UnreadCount { get; set; }
    public int UnreadMentionCount { get; set; }
    public bool MarkedUnread { get; set; }
}

public class MarkUnreadDto
{
    /// <summary>true — mark the chat as unread, false — remove the mark.</summary>
    public bool MarkedUnread { get; set; }
}
