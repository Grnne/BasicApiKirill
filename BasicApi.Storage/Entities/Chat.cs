namespace BasicApi.Storage.Entities;

public class Chat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Title { get; set; } // Для group чатов (опционально)
    public string Type { get; set; } = "private"; // private / group
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}