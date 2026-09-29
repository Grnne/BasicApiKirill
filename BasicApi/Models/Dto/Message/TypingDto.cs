namespace BasicApi.Models.Dto.Message;

public class TypingDto
{
    /// <summary>true — начал или продолжает печатать (повторять раз в несколько секунд), false — перестал.</summary>
    public bool IsTyping { get; set; }
}
