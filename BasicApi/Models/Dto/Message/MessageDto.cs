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

    /// <summary>Номер сообщения в чате: 1, 2, 3, … Порядок сообщений — по нему.</summary>
    public long Seq { get; set; }

    /// <summary>Id, переданный отправителем при отправке через REST; null — не передавался.</summary>
    public Guid? ClientMessageId { get; set; }
}
