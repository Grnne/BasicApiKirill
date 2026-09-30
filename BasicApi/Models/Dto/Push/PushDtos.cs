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

public class PushSubscriptionKeysDto
{
    /// <summary>The browser's P-256 public key, base64url.</summary>
    public string? P256dh { get; set; }

    /// <summary>The shared authentication secret, base64url.</summary>
    public string? Auth { get; set; }
}
