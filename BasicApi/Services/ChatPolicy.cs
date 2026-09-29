using BasicApi.Middleware.Exceptions;
using BasicApi.Storage.Dto;
using Microsoft.Extensions.Options;

namespace BasicApi.Services;

/// <summary>
/// A policy decision: allowed or not, and why — the code goes to the client as is
/// (REST — 403 with this errorCode, hub — <c>"CODE: message"</c>).
/// </summary>
public sealed record PolicyDecision(bool Allowed, string? Code = null, string? Reason = null)
{
    public static readonly PolicyDecision Allow = new(true);
    public static PolicyDecision Deny(string code, string reason) => new(false, code, reason);
}

/// <summary>
/// The single place that decides who may do what in a chat. Services ask the
/// policy and do not check membership themselves. For now the rules are "chat member" and
/// "author of the message"; group permissions, blocks and privacy settings (plan 2) will go here
/// without touching the calling code.
/// </summary>
public interface IChatPolicy
{
    /// <summary>Read history, search, see the member list, subscribe to chat events, mark as read.</summary>
    Task<PolicyDecision> CanReadAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>Write messages and show "typing".</summary>
    Task<PolicyDecision> CanPostAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>Edit the text of a message. The caller has already checked they can post in the chat.</summary>
    Task<PolicyDecision> CanEditMessageAsync(Guid userId, MessageWithSender message, CancellationToken ct = default);

    /// <summary>
    /// Delete a message for everyone. The caller has already checked they can read the chat;
    /// "delete for me" needs nothing more.
    /// </summary>
    Task<PolicyDecision> CanDeleteForEveryoneAsync(Guid userId, MessageWithSender message, CancellationToken ct = default);

    /// <summary>Who sees a user's online status — their changes are broadcast to them.</summary>
    Task<IReadOnlyCollection<Guid>> GetPresenceAudienceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Whose online status from <paramref name="userIds"/> is visible to <paramref name="viewerId"/>.</summary>
    Task<IReadOnlySet<Guid>> FilterPresenceVisibleAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}

public sealed class ChatPolicy(
    IMembershipService membership,
    IOptions<MessageOptions>? options = null,
    TimeProvider? time = null) : IChatPolicy
{
    public const string NotAMemberCode = "NOT_A_MEMBER";
    private const string NotAMemberReason = "User is not a member of this chat";

    public const string NotMessageAuthorCode = "NOT_MESSAGE_AUTHOR";
    public const string EditWindowExpiredCode = "EDIT_WINDOW_EXPIRED";
    public const string DeleteWindowExpiredCode = "DELETE_WINDOW_EXPIRED";
    public const string MessageNotEditableCode = "MESSAGE_NOT_EDITABLE";

    private readonly MessageOptions _messages = options?.Value ?? new MessageOptions();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<PolicyDecision> CanReadAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        MemberOnlyAsync(userId, chatId, ct);

    public Task<PolicyDecision> CanPostAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        MemberOnlyAsync(userId, chatId, ct);

    public Task<PolicyDecision> CanEditMessageAsync(Guid userId, MessageWithSender message, CancellationToken ct = default) =>
        Task.FromResult(message.IsForward && message.SenderId == userId
            // Someone else's words: the one who forwarded them may not change them.
            ? PolicyDecision.Deny(MessageNotEditableCode, "A forwarded message cannot be edited")
            : AuthorWithin(userId, message, _messages.EditWindowHours,
                EditWindowExpiredCode, "The time to edit this message has passed"));

    // Group admins who may delete other members' messages come with groups (F3).
    public Task<PolicyDecision> CanDeleteForEveryoneAsync(Guid userId, MessageWithSender message, CancellationToken ct = default) =>
        Task.FromResult(AuthorWithin(userId, message, _messages.DeleteWindowHours,
            DeleteWindowExpiredCode, "The time to delete this message for everyone has passed"));

    public async Task<IReadOnlyCollection<Guid>> GetPresenceAudienceAsync(Guid userId, CancellationToken ct = default) =>
        await membership.GetContactIdsAsync(userId, ct);

    public async Task<IReadOnlySet<Guid>> FilterPresenceVisibleAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        // Your own status is always visible; someone else's — if there is a shared chat.
        var visible = (await membership.GetContactIdsAsync(viewerId, ct)).ToHashSet();
        visible.Add(viewerId);
        return userIds.Where(visible.Contains).ToHashSet();
    }

    private async Task<PolicyDecision> MemberOnlyAsync(Guid userId, Guid chatId, CancellationToken ct) =>
        await membership.IsMemberAsync(chatId, userId, ct)
            ? PolicyDecision.Allow
            : PolicyDecision.Deny(NotAMemberCode, NotAMemberReason);

    /// <summary>Only the author, and only within the window (0 — no window).</summary>
    private PolicyDecision AuthorWithin(
        Guid userId, MessageWithSender message, int windowHours, string expiredCode, string expiredReason)
    {
        if (message.SenderId != userId)
            return PolicyDecision.Deny(NotMessageAuthorCode, "Only the author can do this with the message");

        var expired = windowHours > 0 &&
                      _time.GetUtcNow().UtcDateTime - message.CreatedAt > TimeSpan.FromHours(windowHours);
        return expired ? PolicyDecision.Deny(expiredCode, expiredReason) : PolicyDecision.Allow;
    }
}

public static class ChatPolicyExtensions
{
    /// <summary>Throws 403 with the code from the policy decision if reading is not allowed.</summary>
    public static async Task DemandReadAsync(this IChatPolicy policy, Guid userId, Guid chatId, CancellationToken ct = default) =>
        (await policy.CanReadAsync(userId, chatId, ct)).Demand();

    /// <summary>Throws 403 with the code from the policy decision if writing is not allowed.</summary>
    public static async Task DemandPostAsync(this IChatPolicy policy, Guid userId, Guid chatId, CancellationToken ct = default) =>
        (await policy.CanPostAsync(userId, chatId, ct)).Demand();

    /// <summary>Throws 403 with the code from the decision if the action is not allowed.</summary>
    public static void Demand(this PolicyDecision decision)
    {
        if (!decision.Allowed)
            throw new ForbiddenException(decision.Reason ?? "Access denied", decision.Code ?? "ACCESS_DENIED");
    }
}
