namespace BasicApi.Models.Dto.Message;

public class MessageDto
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }

    /// <summary>Number of the message in the chat: 1, 2, 3, … Messages are ordered by it.</summary>
    public long Seq { get; set; }

    /// <summary>Id supplied by the sender when sending via REST; null — not supplied.</summary>
    public Guid? ClientMessageId { get; set; }

    /// <summary>Kind of message: <c>text</c> for now.</summary>
    public string Type { get; set; } = "text";

    /// <summary>When the text was last edited; null — never edited.</summary>
    public DateTime? EditedAt { get; set; }
}
