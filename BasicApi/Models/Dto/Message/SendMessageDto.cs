namespace BasicApi.Models.Dto.Message;

public class SendMessageDto
{
    /// <summary>Текст; пробелы по краям обрезаются, после этого — от 1 до 4096 символов.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// Необязательный id, который клиент выбирает сам (новый Guid на каждое сообщение)
    /// и повторяет при ретраях: повтор не создаёт второе сообщение, а возвращает первое.
    /// </summary>
    public Guid? ClientMessageId { get; set; }
}
