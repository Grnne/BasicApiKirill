using BasicApi.Models.Dto.Message;

namespace BasicApi.Features.Messages;

public class SaveDraftDto
{
    /// <summary>Up to 4096 characters; kept as typed. Empty (or only spaces) without a reply — the draft is removed.</summary>
    public string? Text { get; set; }

    /// <summary>Formatting over <see cref="Text"/>; the same rules as when sending.</summary>
    public List<MessageEntityDto>? Entities { get; set; }

    /// <summary>The message being answered; it must be in the chat and not deleted.</summary>
    public Guid? ReplyToMessageId { get; set; }
}
