namespace BasicApi.Models.Dto.Users;

/// <summary>
/// Privacy settings: <c>everybody</c>, <c>contacts</c> (those who share a chat with the user) or
/// <c>nobody</c>. In a request a null field is left as it is.
/// </summary>
public class PrivacySettingsDto
{
    /// <summary>Who sees online and last seen. Hiding one's own hides the others' too.</summary>
    public string? LastSeen { get; set; }

    /// <summary>Who may start a private chat; an existing one keeps working.</summary>
    public string? Messages { get; set; }

    /// <summary>Who may add the user to groups.</summary>
    public string? GroupAdd { get; set; }
}
