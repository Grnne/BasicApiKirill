namespace BasicApi.Services;

/// <summary>Instance-wide message rules, section <c>Messages</c> of the configuration.</summary>
public sealed class MessageOptions
{
    public const string Section = "Messages";

    /// <summary>Used when the configuration does not list reactions.</summary>
    public static readonly string[] DefaultReactions = ["👍", "❤️", "😂", "😮", "😢", "🙏", "👎", "🔥", "🎉"];

    /// <summary>How long the author may edit a message; 0 — no limit.</summary>
    public int EditWindowHours { get; set; } = 48;

    /// <summary>How long the author may delete a message for everyone; 0 — no limit.</summary>
    public int DeleteWindowHours { get; set; } = 48;

    /// <summary>
    /// The reactions users may put. No initializer on purpose: the configuration binder would
    /// append the configured list to it instead of replacing it.
    /// </summary>
    public string[]? Reactions { get; set; }

    public IReadOnlyList<string> AllowedReactions => Reactions is { Length: > 0 } ? Reactions : DefaultReactions;
}
