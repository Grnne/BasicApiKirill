namespace BasicApi.Models.Dto.Message;

public class TypingDto
{
    /// <summary>true - started or keeps typing (repeat every few seconds), false - stopped.</summary>
    public bool IsTyping { get; set; }
}
