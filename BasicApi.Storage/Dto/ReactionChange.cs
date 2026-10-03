namespace BasicApi.Storage.Dto;

/// <summary>Result of setting or removing a reaction.</summary>
/// <param name="Found">The message exists in the chat and is not deleted.</param>
/// <param name="Changed">The user's reaction actually changed (a repeat changes nothing).</param>
/// <param name="SummaryJson">The message's reactions after the change: [{emoji, count}], or null.</param>
/// <param name="AuthorId">Who wrote the message; empty when it is not found.</param>
public sealed record ReactionChange(bool Found, bool Changed, string? SummaryJson, Guid AuthorId = default)
{
    public static readonly ReactionChange NotFound = new(false, false, null);
}
