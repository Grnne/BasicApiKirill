namespace BasicApi.Storage.Dto;

/// <summary>One chat-list row with everything needed to build ChatListItemDto.</summary>
public class ChatListResult
{
    public Guid ChatId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Guid? CompanionId { get; set; }
    public string? CompanionName { get; set; }
    public string? CompanionUsername { get; set; }
    public int UnreadCount { get; set; }
    public int UnreadMentionCount { get; set; }
    public long LastReadSeq { get; set; }
    public bool MarkedUnread { get; set; }

    // The viewer's draft (nullable — none)
    public string? DraftText { get; set; }
    public string? DraftEntitiesJson { get; set; }
    public Guid? DraftReplyToMessageId { get; set; }
    public DateTime? DraftUpdatedAt { get; set; }
    public long OutboxReadSeq { get; set; }
    public long OutboxDeliveredSeq { get; set; }
    public bool HasOthers { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }

    // Last message fields (nullable — chat may have no messages)
    public Guid? LastMessageId { get; set; }
    public long? LastMessageSeq { get; set; }
    public Guid? LastMessageSenderId { get; set; }
    public int? PinnedPosition { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime? MutedUntil { get; set; }

    public Guid? CompanionAvatarId { get; set; }
    public Guid? ChatAvatarId { get; set; }

    public string? LastMessageText { get; set; }
    public string? LastMessageEntitiesJson { get; set; }
    public string? LastMessageType { get; set; }

    /// <summary>The last message's files as a JSON array; null — none.</summary>
    public string? LastMessageAttachmentsJson { get; set; }
    public DateTime? LastMessageCreatedAt { get; set; }
    public string? LastMessageSenderName { get; set; }
    public bool LastMessageIsOwn { get; set; }
}

