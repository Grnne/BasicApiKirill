namespace BasicApi.Storage.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChatId { get; set; }
    public Guid SenderId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = MessageTypes.Text;
}

public static class MessageTypes
{
    public const string Text = "text";
}
