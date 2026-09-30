using BasicApi.Models.Dto.Message;

namespace BasicApi.Models.Dto.Chat;

/// <summary>An unsent message, the same on all the user's devices.</summary>
public class DraftDto
{
    /// <summary>The text as typed, not trimmed.</summary>
    public string Text { get; set; } = string.Empty;

    public List<MessageEntityDto> Entities { get; set; } = [];

    /// <summary>The message the draft answers; null — not a reply.</summary>
    public Guid? ReplyToMessageId { get; set; }

    /// <summary>When the draft was last saved (server time).</summary>
    public DateTime UpdatedAt { get; set; }
}

public class SaveDraftDto
{
    /// <summary>Up to 4096 characters; kept as typed. Empty (or only spaces) without a reply — the draft is removed.</summary>
    public string? Text { get; set; }

    /// <summary>Formatting over <see cref="Text"/>; the same rules as when sending.</summary>
    public List<MessageEntityDto>? Entities { get; set; }

    /// <summary>The message being answered; it must be in the chat and not deleted.</summary>
    public Guid? ReplyToMessageId { get; set; }
}

/// <summary><c>DraftUpdated</c>: the draft of a chat changed on one of the user's devices.</summary>
public class DraftUpdatedDto
{
    public Guid ChatId { get; set; }

    /// <summary>The draft now; null — removed (cleared or sent).</summary>
    public DraftDto? Draft { get; set; }
}
