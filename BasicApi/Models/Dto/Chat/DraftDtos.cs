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

/// <summary><c>DraftUpdated</c>: the draft of a chat changed on one of the user's devices.</summary>
public class DraftUpdatedDto
{
    public Guid ChatId { get; set; }

    /// <summary>The draft now; null — removed (cleared or sent).</summary>
    public DraftDto? Draft { get; set; }
}
