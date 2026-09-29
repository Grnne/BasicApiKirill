namespace BasicApi.Models.Dto.Message;

public class SendMessageDto
{
    /// <summary>Текст; пробелы по краям обрезаются, после этого — от 1 до 4096 символов.</summary>
    public string? Text { get; set; }
}
