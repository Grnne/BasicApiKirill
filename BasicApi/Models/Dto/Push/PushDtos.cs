namespace BasicApi.Models.Dto.Push;

public class PushConfigDto
{
    /// <summary>The server sends push notifications; false — do not offer them.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The server's public VAPID key: <c>applicationServerKey</c> for
    /// <c>pushManager.subscribe</c>. Null when push is off.
    /// </summary>
    public string? VapidPublicKey { get; set; }
}

/// <summary>A browser push subscription, as <c>PushSubscription.toJSON()</c> gives it.</summary>
public class PushSubscriptionDto
{
    /// <summary>The push service address to deliver to.</summary>
    public string? Endpoint { get; set; }

    public PushSubscriptionKeysDto? Keys { get; set; }
}

/// <summary>
/// What a push notification carries — the browser decrypts it and hands it to the service worker's
/// <c>push</c> event (<c>event.data.json()</c>). Enough to show the notification without asking
/// the API: the worker has no access token.
/// </summary>
public class PushNotificationDto
{
    /// <summary><c>message</c> — a new message. More kinds may come: ignore an unknown one.</summary>
    public string Kind { get; set; } = "message";

    public Guid ChatId { get; set; }

    /// <summary><c>private</c> or <c>group</c>.</summary>
    public string ChatType { get; set; } = string.Empty;

    /// <summary>The group's title; null in a private chat — show the sender's name.</summary>
    public string? ChatTitle { get; set; }

    public Guid MessageId { get; set; }
    public long Seq { get; set; }
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;

    /// <summary>As <c>MessageDto.type</c>: <c>text</c>, <c>media</c>, <c>system</c>.</summary>
    public string MessageType { get; set; } = "text";

    /// <summary>The start of the text (up to 100 characters); for media — of the caption, may be empty.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Media: the kind of the first file (<c>photo</c>, <c>video</c>, <c>file</c>, <c>voice</c>); null otherwise.</summary>
    public string? AttachmentKind { get; set; }

    public int AttachmentCount { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class PushSubscriptionKeysDto
{
    /// <summary>The browser's P-256 public key, base64url.</summary>
    public string? P256dh { get; set; }

    /// <summary>The shared authentication secret, base64url.</summary>
    public string? Auth { get; set; }
}
