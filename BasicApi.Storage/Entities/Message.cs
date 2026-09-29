namespace BasicApi.Storage.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChatId { get; set; }
    public Guid SenderId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = MessageTypes.Text;

    /// <summary>Formatting as JSON, already checked by the service; null — plain text.</summary>
    public string? EntitiesJson { get; set; }

    /// <summary>The message this one answers; in the same chat.</summary>
    public Guid? ReplyToMessageId { get; set; }

    /// <summary>For a forward — the original author, chat and message.</summary>
    public Guid? ForwardFromUserId { get; set; }
    public Guid? ForwardFromChatId { get; set; }
    public Guid? ForwardFromMessageId { get; set; }

    /// <summary>What a system message is about, as JSON; null for text messages.</summary>
    public string? ContentJson { get; set; }
}

public static class MessageTypes
{
    public const string Text = "text";

    /// <summary>A record of what happened in a group ("members added" and the like), written by the server.</summary>
    public const string System = "system";
}
