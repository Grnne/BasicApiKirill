using BasicApi.Middleware.Exceptions;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
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
/// policy and do not check membership themselves. The rules: membership, the author of a message,
/// and in groups the member's role and permissions (D8); blocks and privacy settings (plan 2, F5)
/// will go here without touching the calling code.
/// </summary>
public interface IChatPolicy
{
    /// <summary>Read history, search, see the member list, subscribe to chat events, mark as read.</summary>
    Task<PolicyDecision> CanReadAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>Write messages and show "typing"; in a group — with the permission to send.</summary>
    Task<PolicyDecision> CanPostAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>Send files; in a group — with the permissions to send messages and media.</summary>
    Task<PolicyDecision> CanPostMediaAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>Edit the text of a message. The caller has already checked they can post in the chat.</summary>
    Task<PolicyDecision> CanEditMessageAsync(Guid userId, MessageWithSender message, CancellationToken ct = default);

    /// <summary>
    /// Delete a message for everyone: the author within the window, and in a group a member with
    /// the permission to delete others' messages — any message at any time. The caller has already
    /// checked they can read the chat; "delete for me" needs nothing more.
    /// </summary>
    Task<PolicyDecision> CanDeleteForEveryoneAsync(Guid userId, MessageWithSender message, CancellationToken ct = default);

    /// <summary>Put or remove a reaction on a message of the chat.</summary>
    Task<PolicyDecision> CanReactAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>
    /// Manage a group: <paramref name="target"/> is the member the action is about, where there is
    /// one. The caller has already checked that the chat is a group.
    /// </summary>
    Task<PolicyDecision> CanManageAsync(
        Guid userId, Guid chatId, GroupAction action, ChatMember? target = null, CancellationToken ct = default);

    /// <summary>
    /// Start a private chat with <paramref name="otherId"/> (D11): by their <c>messages</c> setting, and
    /// not when they blocked the user. An existing chat is not asked about.
    /// </summary>
    Task<PolicyDecision> CanStartPrivateChatAsync(Guid userId, Guid otherId, CancellationToken ct = default);

    /// <summary>
    /// Of <paramref name="candidateIds"/>, whom <paramref name="userId"/> may not add to a group: by their
    /// <c>groupAdd</c> setting or a block.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetGroupAddRefusalsAsync(
        Guid userId, IReadOnlyCollection<Guid> candidateIds, CancellationToken ct = default);

    /// <summary>Who sees a user's online status — their changes are broadcast to them.</summary>
    Task<IReadOnlyCollection<Guid>> GetPresenceAudienceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Whose online status from <paramref name="userIds"/> is visible to <paramref name="viewerId"/>.</summary>
    Task<IReadOnlySet<Guid>> FilterPresenceVisibleAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}

public sealed class ChatPolicy(
    IMembershipService membership,
    IOptions<MessageOptions>? options = null,
    TimeProvider? time = null,
    IPrivacyRepository? privacy = null) : IChatPolicy
{
    public const string NotAMemberCode = "NOT_A_MEMBER";
    private const string NotAMemberReason = "User is not a member of this chat";

    public const string NotMessageAuthorCode = "NOT_MESSAGE_AUTHOR";
    public const string EditWindowExpiredCode = "EDIT_WINDOW_EXPIRED";
    public const string DeleteWindowExpiredCode = "DELETE_WINDOW_EXPIRED";
    public const string MessageNotEditableCode = "MESSAGE_NOT_EDITABLE";
    public const string PermissionDeniedCode = "PERMISSION_DENIED";
    public const string PrivacyRestrictedCode = "PRIVACY_RESTRICTED";

    private readonly MessageOptions _messages = options?.Value ?? new MessageOptions();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<PolicyDecision> CanReadAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        MemberOnlyAsync(userId, chatId, ct);

    public async Task<PolicyDecision> CanPostAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        await membership.GetMemberAsync(chatId, userId, ct) switch
        {
            null => NotAMember(),
            { ChatType: ChatTypes.Group } member when !GroupRights.Effective(member).SendMessages =>
                PermissionDenied("You may not send messages in this group"),
            _ => PolicyDecision.Allow
        };

    public async Task<PolicyDecision> CanPostMediaAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        await membership.GetMemberAsync(chatId, userId, ct) switch
        {
            null => NotAMember(),
            { ChatType: ChatTypes.Group } member when GroupRights.Effective(member) is var rights &&
                                                      !(rights.SendMessages && rights.SendMedia) =>
                PermissionDenied("You may not send media in this group"),
            _ => PolicyDecision.Allow
        };

    public Task<PolicyDecision> CanReactAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        MemberOnlyAsync(userId, chatId, ct);

    public Task<PolicyDecision> CanEditMessageAsync(Guid userId, MessageWithSender message, CancellationToken ct = default) =>
        Task.FromResult(
            message.Type == MessageTypes.System && message.SenderId == userId
                ? PolicyDecision.Deny(MessageNotEditableCode, "A system message cannot be edited")
            : message.IsForward && message.SenderId == userId
                // Someone else's words: the one who forwarded them may not change them.
                ? PolicyDecision.Deny(MessageNotEditableCode, "A forwarded message cannot be edited")
            : AuthorWithin(userId, message, _messages.EditWindowHours,
                EditWindowExpiredCode, "The time to edit this message has passed"));

    public async Task<PolicyDecision> CanDeleteForEveryoneAsync(
        Guid userId, MessageWithSender message, CancellationToken ct = default)
    {
        // A system message is the group's record, not its author's words: only a moderator removes it.
        var asAuthor = message.Type != MessageTypes.System
            ? AuthorWithin(userId, message, _messages.DeleteWindowHours,
                DeleteWindowExpiredCode, "The time to delete this message for everyone has passed")
            : PolicyDecision.Deny(NotMessageAuthorCode, "Only a group admin can delete a system message");
        if (asAuthor.Allowed)
            return asAuthor;

        return await membership.GetMemberAsync(message.ChatId, userId, ct) is { ChatType: ChatTypes.Group } member &&
               GroupRights.Effective(member).DeleteMessages
            ? PolicyDecision.Allow
            : asAuthor;
    }

    public async Task<PolicyDecision> CanManageAsync(
        Guid userId, Guid chatId, GroupAction action, ChatMember? target = null, CancellationToken ct = default)
    {
        if (await membership.GetMemberAsync(chatId, userId, ct) is not { } actor)
            return NotAMember();

        var isOwner = actor.Role == ChatRoles.Owner;
        var isAdmin = isOwner || actor.Role == ChatRoles.Admin;
        var rights = GroupRights.Effective(actor);
        var targetRole = target?.Role;

        var allowed = action switch
        {
            GroupAction.AddMembers => rights.AddMembers,
            GroupAction.ChangeInfo => rights.ChangeInfo,
            // Removing and restricting: the owner anyone but themselves, an admin with the right — members only.
            GroupAction.RemoveMember or GroupAction.RestrictMember =>
                targetRole != ChatRoles.Owner && (isOwner || targetRole == ChatRoles.Member && rights.RemoveMembers),
            GroupAction.ChangeMemberDefaults => isOwner || isAdmin && rights.RemoveMembers,
            GroupAction.PromoteToAdmin => targetRole == ChatRoles.Member && rights.AddAdmins,
            GroupAction.DemoteAdmin or GroupAction.RestrictAdmin => isOwner && targetRole == ChatRoles.Admin,
            GroupAction.TransferOwnership => isOwner && targetRole is ChatRoles.Admin or ChatRoles.Member,
            GroupAction.DeleteGroup => isOwner,
            GroupAction.ViewAudit => isAdmin,
            _ => false
        };

        return allowed ? PolicyDecision.Allow : PermissionDenied(DeniedReason(action));
    }

    private static string DeniedReason(GroupAction action) => action switch
    {
        GroupAction.AddMembers => "You may not add members to this group",
        GroupAction.ChangeInfo => "You may not change this group",
        GroupAction.RemoveMember => "You may not remove this member",
        GroupAction.RestrictMember or GroupAction.RestrictAdmin => "You may not change this member's permissions",
        GroupAction.ChangeMemberDefaults => "You may not change the members' permissions",
        GroupAction.PromoteToAdmin or GroupAction.DemoteAdmin => "You may not change this member's role",
        GroupAction.TransferOwnership or GroupAction.DeleteGroup => "Only the owner can do this",
        GroupAction.ViewAudit => "Only admins can see the group's actions",
        _ => "Access denied"
    };

    public async Task<PolicyDecision> CanStartPrivateChatAsync(Guid userId, Guid otherId, CancellationToken ct = default)
    {
        if (privacy is null)
            return PolicyDecision.Allow;

        // Which of the two it is — a setting or a block — is not told: a block stays unnoticed.
        var denied = PolicyDecision.Deny(PrivacyRestrictedCode, "This user does not accept messages from you");
        if (await privacy.IsBlockedAsync(otherId, userId, ct))
            return denied;
        return (await privacy.GetAsync(otherId, ct)).Messages switch
        {
            PrivacyLevels.Nobody => denied,
            PrivacyLevels.Contacts when !await privacy.ShareChatAsync(userId, otherId, ct) => denied,
            _ => PolicyDecision.Allow
        };
    }

    public async Task<IReadOnlyList<Guid>> GetGroupAddRefusalsAsync(
        Guid userId, IReadOnlyCollection<Guid> candidateIds, CancellationToken ct = default) =>
        privacy is null || candidateIds.Count == 0 ? [] : await privacy.GetGroupAddRefusalsAsync(userId, candidateIds, ct);

    /// <remarks>
    /// Presence is mutual: contacts who neither hide it (D11) nor blocked one another. Without the
    /// privacy store (unit tests) — all contacts.
    /// </remarks>
    public async Task<IReadOnlyCollection<Guid>> GetPresenceAudienceAsync(Guid userId, CancellationToken ct = default) =>
        privacy is null
            ? await membership.GetContactIdsAsync(userId, ct)
            : await privacy.GetPresencePeersAsync(userId, ct: ct);

    public async Task<IReadOnlySet<Guid>> FilterPresenceVisibleAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        // Your own status is always visible; someone else's — by the same mutual rule.
        var visible = (privacy is null
            ? await membership.GetContactIdsAsync(viewerId, ct)
            : await privacy.GetPresencePeersAsync(viewerId, userIds, ct)).ToHashSet();
        visible.Add(viewerId);
        return userIds.Where(visible.Contains).ToHashSet();
    }

    private async Task<PolicyDecision> MemberOnlyAsync(Guid userId, Guid chatId, CancellationToken ct) =>
        await membership.IsMemberAsync(chatId, userId, ct) ? PolicyDecision.Allow : NotAMember();

    private static PolicyDecision NotAMember() => PolicyDecision.Deny(NotAMemberCode, NotAMemberReason);

    private static PolicyDecision PermissionDenied(string reason) => PolicyDecision.Deny(PermissionDeniedCode, reason);

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

/// <summary>What is done to a group; see <see cref="IChatPolicy.CanManageAsync"/>.</summary>
public enum GroupAction
{
    AddMembers,
    RemoveMember,
    /// <summary>Change the title.</summary>
    ChangeInfo,
    /// <summary>Change what members may do by default.</summary>
    ChangeMemberDefaults,
    /// <summary>Change a member's own permissions.</summary>
    RestrictMember,
    /// <summary>Change an admin's permissions.</summary>
    RestrictAdmin,
    PromoteToAdmin,
    DemoteAdmin,
    TransferOwnership,
    DeleteGroup,
    ViewAudit
}

public static class ChatPolicyExtensions
{
    /// <summary>Throws 403 with the code from the policy decision if reading is not allowed.</summary>
    public static async Task DemandReadAsync(this IChatPolicy policy, Guid userId, Guid chatId, CancellationToken ct = default) =>
        (await policy.CanReadAsync(userId, chatId, ct)).Demand();

    /// <summary>Throws 403 with the code from the policy decision if writing is not allowed.</summary>
    public static async Task DemandPostAsync(this IChatPolicy policy, Guid userId, Guid chatId, CancellationToken ct = default) =>
        (await policy.CanPostAsync(userId, chatId, ct)).Demand();

    /// <summary>Throws 403 with the code from the policy decision if sending files is not allowed.</summary>
    public static async Task DemandPostMediaAsync(this IChatPolicy policy, Guid userId, Guid chatId, CancellationToken ct = default) =>
        (await policy.CanPostMediaAsync(userId, chatId, ct)).Demand();

    /// <summary>Throws 403 with the code from the decision if the action is not allowed.</summary>
    public static void Demand(this PolicyDecision decision)
    {
        if (!decision.Allowed)
            throw new ForbiddenException(decision.Reason ?? "Access denied", decision.Code ?? "ACCESS_DENIED");
    }
}
