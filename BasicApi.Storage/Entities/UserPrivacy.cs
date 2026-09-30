namespace BasicApi.Storage.Entities;

/// <summary>A user's privacy settings (D11).</summary>
public class UserPrivacy
{
    public Guid UserId { get; set; }

    /// <summary>Who sees online and last seen.</summary>
    public string LastSeen { get; set; } = PrivacyLevels.Everybody;

    /// <summary>Who may start a private chat; an existing one keeps working.</summary>
    public string Messages { get; set; } = PrivacyLevels.Everybody;

    /// <summary>Who may add the user to groups.</summary>
    public string GroupAdd { get; set; } = PrivacyLevels.Everybody;

    public DateTime? UpdatedAt { get; set; }
}

public static class PrivacyLevels
{
    public const string Everybody = "everybody";

    /// <summary>Those who share a chat with the user — the product has no contact list.</summary>
    public const string Contacts = "contacts";

    public const string Nobody = "nobody";

    public static bool IsValid(string? level) => level is Everybody or Contacts or Nobody;
}
