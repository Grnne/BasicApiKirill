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

    /// <summary>Deleted for everyone: a tombstone without text.</summary>
    public DateTime? DeletedAt { get; set; }

    public long Seq { get; set; }
    public Guid? ClientMessageId { get; set; }
    public string SenderName { get; set; } = string.Empty;
}
