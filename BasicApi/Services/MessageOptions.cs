namespace BasicApi.Services;

/// <summary>Instance-wide message rules, section <c>Messages</c> of the configuration.</summary>
public sealed class MessageOptions
{
    public const string Section = "Messages";

    /// <summary>How long the author may edit a message; 0 — no limit.</summary>
    public int EditWindowHours { get; set; } = 48;

    /// <summary>How long the author may delete a message for everyone; 0 — no limit.</summary>
    public int DeleteWindowHours { get; set; } = 48;
}
