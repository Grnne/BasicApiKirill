namespace BasicApi.Storage.Entities;

/// <summary>An unsent message the user is writing in a chat.</summary>
public class Draft
{
    public Guid UserId { get; set; }
    public Guid ChatId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? EntitiesJson { get; set; }
    public Guid? ReplyToMessageId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
