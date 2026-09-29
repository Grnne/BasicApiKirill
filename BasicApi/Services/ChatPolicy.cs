using BasicApi.Middleware.Exceptions;

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
/// policy and do not check membership themselves. For now there is one rule — "chat member";
/// group permissions, blocks and privacy settings (plan 2) will go here
/// without touching the calling code. Permissions for editing messages and managing members
/// will appear together with those features.
/// </summary>
public interface IChatPolicy
{
    /// <summary>Read history, search, see the member list, subscribe to chat events, mark as read.</summary>
    Task<PolicyDecision> CanReadAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>Write messages and show "typing".</summary>
    Task<PolicyDecision> CanPostAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>Who sees a user's online status — their changes are broadcast to them.</summary>
    Task<IReadOnlyCollection<Guid>> GetPresenceAudienceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Whose online status from <paramref name="userIds"/> is visible to <paramref name="viewerId"/>.</summary>
    Task<IReadOnlySet<Guid>> FilterPresenceVisibleAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}

public sealed class ChatPolicy(IMembershipService membership) : IChatPolicy
{
    public const string NotAMemberCode = "NOT_A_MEMBER";
    private const string NotAMemberReason = "User is not a member of this chat";

    public Task<PolicyDecision> CanReadAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        MemberOnlyAsync(userId, chatId, ct);

    public Task<PolicyDecision> CanPostAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        MemberOnlyAsync(userId, chatId, ct);

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
}

public static class ChatPolicyExtensions
{
    /// <summary>Throws 403 with the code from the policy decision if reading is not allowed.</summary>
    public static async Task DemandReadAsync(this IChatPolicy policy, Guid userId, Guid chatId, CancellationToken ct = default) =>
        Demand(await policy.CanReadAsync(userId, chatId, ct));

    /// <summary>Throws 403 with the code from the policy decision if writing is not allowed.</summary>
    public static async Task DemandPostAsync(this IChatPolicy policy, Guid userId, Guid chatId, CancellationToken ct = default) =>
        Demand(await policy.CanPostAsync(userId, chatId, ct));

    private static void Demand(PolicyDecision decision)
    {
        if (!decision.Allowed)
            throw new ForbiddenException(decision.Reason ?? "Access denied", decision.Code ?? "ACCESS_DENIED");
    }
}
