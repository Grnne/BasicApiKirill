namespace BasicApi.Storage.Dto;

/// <summary>What the global search looks for; null filters are not applied.</summary>
public sealed class MessageSearchFilter
{
    public Guid? ChatId { get; set; }
    public Guid? SenderId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>A message type — <c>text</c> or <c>media</c>; null — both.</summary>
    public string? Type { get; set; }
}

/// <summary>A found message with what the client needs to show its chat.</summary>
public sealed class MessageSearchHit : MessageWithSender
{
    public string ChatType { get; set; } = string.Empty;
    public string? ChatTitle { get; set; }
    public Guid? CompanionId { get; set; }
    public string? CompanionName { get; set; }
    public Guid? ChatAvatarId { get; set; }
}
