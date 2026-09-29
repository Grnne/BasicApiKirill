namespace BasicApi.Storage.Dto;

/// <summary>
/// Message entity joined with sender's display name — returned from batched queries
/// to avoid N+1 lookups for each message's sender name.
/// </summary>
public class MessageWithSender
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public Guid SenderId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Type { get; set; } = Entities.MessageTypes.Text;
    public DateTime? EditedAt { get; set; }

    /// <summary>Formatting as JSON; null — plain text.</summary>
    public string? EntitiesJson { get; set; }

    /// <summary>Deleted for everyone: a tombstone without text.</summary>
    public DateTime? DeletedAt { get; set; }

    public long Seq { get; set; }
    public Guid? ClientMessageId { get; set; }
    public string SenderName { get; set; } = string.Empty;

    /// <summary>The answered message, joined: its author and text (empty when it is deleted).</summary>
    public Guid? ReplyToMessageId { get; set; }
    public Guid? ReplyToSenderId { get; set; }
    public string? ReplyToSenderName { get; set; }
    public string? ReplyToText { get; set; }
    public bool ReplyToDeleted { get; set; }

    /// <summary>For a forward — the original author (with the name) and where it came from.</summary>
    public Guid? ForwardFromUserId { get; set; }
    public string? ForwardFromUserName { get; set; }
    public Guid? ForwardFromChatId { get; set; }
    public Guid? ForwardFromMessageId { get; set; }

    /// <summary>A copy of another message (its links may be cleared if the original is gone).</summary>
    public bool IsForward => ForwardFromUserId is not null || ForwardFromChatId is not null || ForwardFromMessageId is not null;
}
