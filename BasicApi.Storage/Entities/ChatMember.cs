namespace BasicApi.Storage.Entities;

public class ChatMember
{
    public Guid ChatId { get; set; }
    public Guid UserId { get; set; }
    public Guid? LastReadMessageId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}