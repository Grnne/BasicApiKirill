using System.Text.Json;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services.Events;
using BasicApi.Services.Media;
using BasicApi.Storage;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Options;

namespace BasicApi.Services;

/// <summary>
/// Groups (D8): creating them and managing members, roles and settings. What a member may do is
/// decided by <see cref="IChatPolicy"/>; every change is written to the group's action log and
/// recorded in the chat by a system message where members should see it.
/// </summary>
public interface IGroupService
{
    /// <summary>
    /// Creates a group with the caller as its owner. Everyone added gets <c>ChatCreated</c>; the
    /// caller's other devices learn of it through sync. Errors: 400 <c>INVALID_TITLE</c>/
    /// <c>TOO_MANY_MEMBERS</c>, 404 <c>USER_NOT_FOUND</c> (missing or deactivated).
    /// </summary>
    Task<ChatListItemDto> CreateAsync(Guid creatorId, string? title, IReadOnlyList<Guid>? memberIds, CancellationToken ct = default);

    /// <summary>Members with roles and what each may do. Errors: 400 <c>NOT_A_GROUP</c>, 403 <c>NOT_A_MEMBER</c>.</summary>
    Task<IReadOnlyList<GroupMemberDto>> GetMembersAsync(Guid chatId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Makes a member an admin (owner, or an admin allowed to add admins), an admin a member again
    /// (owner), or hands the group over (<c>owner</c>: the owner becomes an admin). A new role
    /// starts without the member's own overrides. Members get <c>MemberUpdated</c>; the same role
    /// again changes nothing. Errors: 400 <c>NOT_A_GROUP</c>/<c>INVALID_ROLE</c>,
    /// 403 <c>NOT_A_MEMBER</c>/<c>PERMISSION_DENIED</c>, 404 <c>MEMBER_NOT_FOUND</c>.
    /// </summary>
    Task<GroupMemberDto> SetRoleAsync(Guid chatId, Guid userId, Guid targetId, string? role, CancellationToken ct = default);

    /// <summary>
    /// Replaces the member's own permission overrides: given fields override the role and the
    /// group's defaults, omitted ones follow them. A member's — by the owner or an admin allowed to
    /// remove members, and only member permissions; an admin's — by the owner. Members get
    /// <c>MemberUpdated</c>. Errors: 400 <c>NOT_A_GROUP</c>/<c>INVALID_PERMISSIONS</c>,
    /// 403 <c>NOT_A_MEMBER</c>/<c>PERMISSION_DENIED</c>, 404 <c>MEMBER_NOT_FOUND</c>.
    /// </summary>
    Task<GroupMemberDto> SetPermissionsAsync(
        Guid chatId, Guid userId, Guid targetId, PermissionsPatchDto? permissions, CancellationToken ct = default);

    /// <summary>
    /// Adds members (those already in are skipped) with a system message. They get
    /// <c>ChatCreated</c>, the members already there — <c>MemberAdded</c>. Returns who was added.
    /// Errors: 400 <c>NOT_A_GROUP</c>/<c>INVALID_REQUEST</c>/<c>TOO_MANY_MEMBERS</c>,
    /// 403 <c>NOT_A_MEMBER</c>/<c>PERMISSION_DENIED</c>, 404 <c>USER_NOT_FOUND</c>.
    /// </summary>
    Task<IReadOnlyList<GroupMemberDto>> AddMembersAsync(
        Guid chatId, Guid userId, IReadOnlyList<Guid>? userIds, CancellationToken ct = default);

    /// <summary>
    /// Removes a member (or the caller themselves — then it is leaving) with a system message.
    /// Everyone, the removed one included, gets <c>MemberRemoved</c>; the removed one loses the chat.
    /// Errors: 400 <c>NOT_A_GROUP</c>, 403 <c>NOT_A_MEMBER</c>/<c>PERMISSION_DENIED</c>,
    /// 404 <c>MEMBER_NOT_FOUND</c>.
    /// </summary>
    Task RemoveMemberAsync(Guid chatId, Guid userId, Guid targetId, CancellationToken ct = default);

    /// <summary>
    /// Leaves the group. The owner hands it over to the longest-standing admin, or else member;
    /// the last one to leave deletes the group. Errors: 400 <c>NOT_A_GROUP</c>, 403 <c>NOT_A_MEMBER</c>.
    /// </summary>
    Task LeaveAsync(Guid chatId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Changes the title (needs <c>changeInfo</c>; a system message) and what members may do by
    /// default (the owner or an admin with <c>removeMembers</c>; only member permissions). Given
    /// fields change, the rest stay. Members get <c>ChatUpdated</c>; no change — no event.
    /// Errors: 400 <c>NOT_A_GROUP</c>/<c>INVALID_TITLE</c>/<c>INVALID_PERMISSIONS</c>,
    /// 403 <c>NOT_A_MEMBER</c>/<c>PERMISSION_DENIED</c>.
    /// </summary>
    Task<ChatUpdatedDto> UpdateAsync(
        Guid chatId, Guid userId, string? title, PermissionsPatchDto? memberPermissions, CancellationToken ct = default);

    /// <summary>
    /// Sets the group's photo to one the user uploaded, or clears it — with the right to change the
    /// group's info. Members get a system message and <c>ChatUpdated</c>.
    /// Errors: 400 <c>NOT_A_GROUP</c>/<c>INVALID_AVATAR</c>, 403 <c>NOT_A_MEMBER</c>/<c>PERMISSION_DENIED</c>,
    /// 404 <c>ATTACHMENT_NOT_FOUND</c>.
    /// </summary>
    Task<ChatUpdatedDto> SetAvatarAsync(Guid chatId, Guid userId, Guid? attachmentId, CancellationToken ct = default);

    /// <summary>
    /// Deletes the group with its history for everyone (the owner). Members get <c>ChatDeleted</c>.
    /// Errors: 400 <c>NOT_A_GROUP</c>, 403 <c>NOT_A_MEMBER</c>/<c>PERMISSION_DENIED</c>.
    /// </summary>
    Task DeleteAsync(Guid chatId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The group's action log for its owner and admins, newest first; <paramref name="cursor"/> is
    /// the <c>nextCursor</c> of the previous page. Errors: 400 <c>NOT_A_GROUP</c>/<c>INVALID_CURSOR</c>,
    /// 403 <c>NOT_A_MEMBER</c>/<c>PERMISSION_DENIED</c>.
    /// </summary>
    Task<AuditPageDto> GetAuditAsync(Guid chatId, Guid userId, string? cursor, int limit, CancellationToken ct = default);
}

public sealed class GroupService(
    IDbSession db,
    IGroupRepository groups,
    IChatRepository chats,
    IUserRepository users,
    IMessageRepository messages,
    IChatPolicy policy,
    IPresenceService presence,
    IChatEventPublisher events,
    IAttachmentRepository attachments,
    IPrivacyRepository privacy,
    IOptions<GroupOptions> options,
    TimeProvider? time = null) : IGroupService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly GroupOptions _options = options.Value;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<ChatListItemDto> CreateAsync(
        Guid creatorId, string? title, IReadOnlyList<Guid>? memberIds, CancellationToken ct = default)
    {
        var name = NormalizeTitle(title);
        var invited = (memberIds ?? []).Where(id => id != creatorId).Distinct().ToList();
        if (invited.Count + 1 > _options.MaxMembers)
            throw TooManyMembers();
        await DemandActiveUsersAsync(invited, ct);
        await DemandMayAddAsync(creatorId, invited, ct);

        var chatId = Guid.NewGuid();
        var now = Now();
        var card = await db.InTransactionAsync(async ct =>
        {
            await groups.CreateAsync(chatId, name, creatorId, invited, now, ct);

            // Not announced as a new message: the card of the new chat already shows it, and the
            // current client would count it as one more unread on top of the card.
            var system = await PostSystemMessageAsync(chatId, creatorId, SystemMessages.Created(name), now, recipients: [], ct);
            await AuditAsync(chatId, creatorId, "group_created", null, new { title = name, memberIds = invited }, now, ct);

            var own = await CardAsync(chatId, creatorId, ct);
            if (invited.Count > 0)
                await events.ChatCreatedAsync(invited, await CardAsync(chatId, invited[0], ct), ct);
            await events.ChatCreatedAsync(creatorId, own, live: false, ct);
            // The card holds only a preview of it: the journal gets it whole, after the card.
            await events.MessageJournaledAsync(system, [creatorId, .. invited], ct);
            return own;
        }, ct: ct);

        await presence.IntroduceAsync([creatorId, .. invited], [], CancellationToken.None);
        return card;
    }

    public async Task<IReadOnlyList<GroupMemberDto>> GetMembersAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        await DemandGroupAsync(chatId, userId, ct);
        var members = await groups.GetMembersAsync(chatId, ct);
        // Those who blocked the caller do not show their avatar to them.
        var hidden = await privacy.GetBlockersAsync(userId, [.. members.Select(m => m.UserId)], ct);
        return [.. members.Select(m => ToDto(m) is var dto && hidden.Contains(m.UserId) ? WithoutAvatar(dto) : dto)];
    }

    public async Task<GroupMemberDto> SetRoleAsync(
        Guid chatId, Guid userId, Guid targetId, string? role, CancellationToken ct = default)
    {
        if (!ChatRoles.IsValid(role))
            throw new BadRequestException("Role must be owner, admin or member", "INVALID_ROLE");
        await DemandGroupAsync(chatId, userId, ct);

        var now = Now();
        return await db.InTransactionAsync(async ct =>
        {
            // Role changes of one group go one at a time: two admins cannot both hand it over.
            await groups.LockAsync(chatId, ct);
            var target = await MemberAsync(chatId, targetId, ct);
            if (target.Role == role)
                return ToDto(target);

            if (target.Role == ChatRoles.Owner)
                throw new ForbiddenException(
                    "The owner's role changes only by handing the group over", ChatPolicy.PermissionDeniedCode);
            var action = role switch
            {
                ChatRoles.Owner => GroupAction.TransferOwnership,
                ChatRoles.Admin => GroupAction.PromoteToAdmin,
                _ => GroupAction.DemoteAdmin
            };
            (await policy.CanManageAsync(userId, chatId, action, target, ct)).Demand();

            var memberIds = await chats.GetMemberIdsAsync(chatId, ct);
            if (action == GroupAction.TransferOwnership)
            {
                // The old owner steps down first: one owner per group at any moment.
                await groups.SetRoleAsync(chatId, userId, ChatRoles.Admin, ct);
                await groups.SetPermissionsAsync(chatId, userId, null, ct);
                await AuditAsync(chatId, userId, "ownership_transferred", targetId, null, now, ct);
                await events.MemberUpdatedAsync(
                    new MemberUpdatedDto { ChatId = chatId, Member = ToDto(await MemberAsync(chatId, userId, ct)) }, memberIds, ct);
            }
            else
            {
                await AuditAsync(chatId, userId, "role_changed", targetId, new { role }, now, ct);
            }

            await groups.SetRoleAsync(chatId, targetId, role!, ct);
            await groups.SetPermissionsAsync(chatId, targetId, null, ct);
            var updated = ToDto(await MemberAsync(chatId, targetId, ct));
            await events.MemberUpdatedAsync(new MemberUpdatedDto { ChatId = chatId, Member = updated }, memberIds, ct);
            return updated;
        }, ct: ct);
    }

    public async Task<GroupMemberDto> SetPermissionsAsync(
        Guid chatId, Guid userId, Guid targetId, PermissionsPatchDto? permissions, CancellationToken ct = default)
    {
        await DemandGroupAsync(chatId, userId, ct);

        var now = Now();
        return await db.InTransactionAsync(async ct =>
        {
            await groups.LockAsync(chatId, ct);
            var target = await MemberAsync(chatId, targetId, ct);
            var action = target.Role == ChatRoles.Admin ? GroupAction.RestrictAdmin : GroupAction.RestrictMember;
            (await policy.CanManageAsync(userId, chatId, action, target, ct)).Demand();
            if (target.Role == ChatRoles.Member && permissions is not null && GroupRights.TouchesAdminPermissions(permissions))
                throw new BadRequestException(
                    "A member cannot get admin permissions; make them an admin instead", "INVALID_PERMISSIONS");

            var json = GroupRights.WritePatch(permissions);
            if (json == GroupRights.WritePatch(GroupRights.ReadPatch(target.PermissionsJson)))
                return ToDto(target);

            await groups.SetPermissionsAsync(chatId, targetId, json, ct);
            await AuditAsync(chatId, userId, "permissions_changed", targetId,
                new { permissions = GroupRights.ReadPatch(json) }, now, ct);

            var updated = ToDto(await MemberAsync(chatId, targetId, ct));
            await events.MemberUpdatedAsync(
                new MemberUpdatedDto { ChatId = chatId, Member = updated }, await chats.GetMemberIdsAsync(chatId, ct), ct);
            return updated;
        }, ct: ct);
    }

    public async Task<IReadOnlyList<GroupMemberDto>> AddMembersAsync(
        Guid chatId, Guid userId, IReadOnlyList<Guid>? userIds, CancellationToken ct = default)
    {
        var invited = (userIds ?? []).Where(id => id != userId).Distinct().ToList();
        if (invited.Count == 0)
            throw new BadRequestException("userIds must name at least one other user", "INVALID_REQUEST");
        await DemandGroupAsync(chatId, userId, ct);
        var names = (await DemandActiveUsersAsync(invited, ct)).ToDictionary(u => u.Id, u => u.DisplayName);

        var now = Now();
        var (added, before) = await db.InTransactionAsync(async ct =>
        {
            // One membership change of the group at a time: the limit holds under concurrent adds.
            var count = await groups.LockAsync(chatId, ct) ?? 0;
            (await policy.CanManageAsync(userId, chatId, GroupAction.AddMembers, ct: ct)).Demand();

            var before = await chats.GetMemberIdsAsync(chatId, ct);
            var fresh = invited.Except(before).ToList();
            if (fresh.Count == 0)
                return ((IReadOnlyList<GroupMemberDto>)[], before);
            await DemandMayAddAsync(userId, fresh, ct);
            if (count + fresh.Count > _options.MaxMembers)
                throw TooManyMembers();

            var added = await groups.AddMembersAsync(chatId, fresh, now, ct);
            await PostSystemMessageAsync(chatId, userId,
                SystemMessages.Added([.. added.Select(id => (id, names[id]))]), now, recipients: before, ct);
            await AuditAsync(chatId, userId, "members_added", null, new { userIds = added }, now, ct);

            // The new members all see the chat alike: one card for all of them.
            await events.ChatCreatedAsync(added, await CardAsync(chatId, added[0], ct), ct);
            var members = await groups.GetMembersAsync(chatId, ct);
            var dtos = members.Where(m => added.Contains(m.UserId)).Select(ToDto).ToList();
            await events.MembersAddedAsync(new MembersAddedDto { ChatId = chatId, AddedBy = userId, Members = dtos }, before, ct);
            return ((IReadOnlyList<GroupMemberDto>)dtos, before);
        }, ct: ct);

        if (added.Count > 0)
            await presence.IntroduceAsync([.. added.Select(m => m.UserId)], before, CancellationToken.None);
        return added;
    }

    public async Task RemoveMemberAsync(Guid chatId, Guid userId, Guid targetId, CancellationToken ct = default)
    {
        if (targetId == userId)
        {
            await LeaveAsync(chatId, userId, ct);
            return;
        }
        await DemandGroupAsync(chatId, userId, ct);

        var now = Now();
        await db.InTransactionAsync(async ct =>
        {
            await groups.LockAsync(chatId, ct);
            var target = await MemberAsync(chatId, targetId, ct);
            (await policy.CanManageAsync(userId, chatId, GroupAction.RemoveMember, target, ct)).Demand();

            await groups.RemoveMemberAsync(chatId, targetId, ct);
            var remaining = await chats.GetMemberIdsAsync(chatId, ct);
            await PostSystemMessageAsync(chatId, userId, SystemMessages.Removed(targetId, target.DisplayName), now, remaining, ct);
            await AuditAsync(chatId, userId, "member_removed", targetId, null, now, ct);
            await events.MemberRemovedAsync(
                new MemberRemovedDto { ChatId = chatId, UserId = targetId, RemovedBy = userId }, [.. remaining, targetId], ct);
            return true;
        }, ct: ct);
    }

    public async Task LeaveAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        await DemandGroupAsync(chatId, userId, ct);

        var now = Now();
        await db.InTransactionAsync(async ct =>
        {
            await groups.LockAsync(chatId, ct);
            var me = await MemberAsync(chatId, userId, ct);
            var others = (await groups.GetMembersAsync(chatId, ct)).Where(m => m.UserId != userId).ToList();
            if (others.Count == 0)
            {
                // Nobody left to hand it to: the group goes with its last member.
                await groups.DeleteAsync(chatId, ct);
                await events.MemberRemovedAsync(new MemberRemovedDto { ChatId = chatId, UserId = userId }, [userId], ct);
                return true;
            }

            await groups.RemoveMemberAsync(chatId, userId, ct);
            var remaining = others.Select(m => m.UserId).ToList();
            if (me.Role == ChatRoles.Owner)
            {
                // Members come ordered: admins first, each group by joining time.
                var heir = others.FirstOrDefault(m => m.Role == ChatRoles.Admin) ?? others[0];
                await groups.SetRoleAsync(chatId, heir.UserId, ChatRoles.Owner, ct);
                await groups.SetPermissionsAsync(chatId, heir.UserId, null, ct);
                await AuditAsync(chatId, userId, "ownership_transferred", heir.UserId, new { reason = "owner_left" }, now, ct);
                await events.MemberUpdatedAsync(
                    new MemberUpdatedDto { ChatId = chatId, Member = ToDto(await MemberAsync(chatId, heir.UserId, ct)) },
                    remaining, ct);
            }

            await PostSystemMessageAsync(chatId, userId, SystemMessages.Left(userId, me.DisplayName), now, remaining, ct);
            await AuditAsync(chatId, userId, "member_left", userId, null, now, ct);
            await events.MemberRemovedAsync(
                new MemberRemovedDto { ChatId = chatId, UserId = userId }, [.. remaining, userId], ct);
            return true;
        }, ct: ct);
    }

    public async Task<ChatUpdatedDto> UpdateAsync(
        Guid chatId, Guid userId, string? title, PermissionsPatchDto? memberPermissions, CancellationToken ct = default)
    {
        var name = title is null ? null : NormalizeTitle(title);
        if (memberPermissions is not null && GroupRights.TouchesAdminPermissions(memberPermissions))
            throw new BadRequestException("Members cannot get admin permissions by default", "INVALID_PERMISSIONS");
        await DemandGroupAsync(chatId, userId, ct);

        var now = Now();
        return await db.InTransactionAsync(async ct =>
        {
            await groups.LockAsync(chatId, ct);
            var chat = await chats.GetByIdAsync(chatId, ct) ?? throw new NotFoundException("Chat not found", "CHAT_NOT_FOUND");
            var settings = GroupRights.ReadSettings(chat.SettingsJson);
            var renamed = name is not null && name != chat.Title;
            var merged = memberPermissions is null ? settings.MemberPermissions : GroupRights.Merge(settings.MemberPermissions, memberPermissions);
            var regranted = GroupRights.WritePatch(merged) != GroupRights.WritePatch(settings.MemberPermissions);

            if (renamed)
                (await policy.CanManageAsync(userId, chatId, GroupAction.ChangeInfo, ct: ct)).Demand();
            if (regranted)
                (await policy.CanManageAsync(userId, chatId, GroupAction.ChangeMemberDefaults, ct: ct)).Demand();

            settings.MemberPermissions = merged;
            var current = new ChatUpdatedDto
            {
                ChatId = chatId,
                Title = renamed ? name : chat.Title,
                MemberPermissions = GroupRights.MemberPermissions(GroupRights.WriteSettings(settings)),
                AvatarId = chat.AvatarAttachmentId
            };
            if (!renamed && !regranted)
                return current;

            await groups.UpdateAsync(chatId, current.Title!, GroupRights.WriteSettings(settings), now, ct);
            var memberIds = await chats.GetMemberIdsAsync(chatId, ct);
            if (renamed)
            {
                await PostSystemMessageAsync(chatId, userId, SystemMessages.Renamed(name!), now, memberIds, ct);
                await AuditAsync(chatId, userId, "title_changed", null, new { from = chat.Title, to = name }, now, ct);
            }
            if (regranted)
                await AuditAsync(chatId, userId, "member_permissions_changed", null, new { memberPermissions = merged }, now, ct);

            await events.ChatUpdatedAsync(current, memberIds, ct);
            return current;
        }, ct: ct);
    }

    public async Task<ChatUpdatedDto> SetAvatarAsync(
        Guid chatId, Guid userId, Guid? attachmentId, CancellationToken ct = default)
    {
        await DemandGroupAsync(chatId, userId, ct);
        if (attachmentId is { } id)
            await attachments.DemandOwnPhotoAsync(userId, id, ct);

        var now = Now();
        return await db.InTransactionAsync(async ct =>
        {
            await groups.LockAsync(chatId, ct);
            (await policy.CanManageAsync(userId, chatId, GroupAction.ChangeInfo, ct: ct)).Demand();
            var chat = await chats.GetByIdAsync(chatId, ct) ?? throw new NotFoundException("Chat not found", "CHAT_NOT_FOUND");
            var current = new ChatUpdatedDto
            {
                ChatId = chatId,
                Title = chat.Title,
                MemberPermissions = GroupRights.MemberPermissions(chat.SettingsJson),
                AvatarId = attachmentId
            };
            if (!await groups.SetAvatarAsync(chatId, attachmentId, now, ct))
                return current;

            var memberIds = await chats.GetMemberIdsAsync(chatId, ct);
            await PostSystemMessageAsync(chatId, userId, SystemMessages.Photo(removed: attachmentId is null), now, memberIds, ct);
            await AuditAsync(chatId, userId, attachmentId is null ? "photo_removed" : "photo_changed", null,
                new { attachmentId }, now, ct);
            await events.ChatUpdatedAsync(current, memberIds, ct);
            return current;
        }, ct: ct);
    }

    public async Task DeleteAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        await DemandGroupAsync(chatId, userId, ct);

        await db.InTransactionAsync(async ct =>
        {
            await groups.LockAsync(chatId, ct);
            (await policy.CanManageAsync(userId, chatId, GroupAction.DeleteGroup, ct: ct)).Demand();

            var memberIds = await chats.GetMemberIdsAsync(chatId, ct);
            await groups.DeleteAsync(chatId, ct);
            await events.ChatDeletedAsync(new ChatDeletedDto { ChatId = chatId }, memberIds, ct);
            return true;
        }, ct: ct);
    }

    public async Task<AuditPageDto> GetAuditAsync(
        Guid chatId, Guid userId, string? cursor, int limit, CancellationToken ct = default)
    {
        // The cursor is the id of the last entry of the previous page: entries only get appended.
        long? beforeId = null;
        if (!string.IsNullOrEmpty(cursor))
            beforeId = long.TryParse(cursor, out var id) && id > 0
                ? id
                : throw new BadRequestException("Cursor is malformed", "INVALID_CURSOR");

        await DemandGroupAsync(chatId, userId, ct);
        (await policy.CanManageAsync(userId, chatId, GroupAction.ViewAudit, ct: ct)).Demand();

        var rows = await groups.GetAuditAsync(chatId, beforeId, limit + 1, ct);
        var page = rows.Take(limit).ToList();
        var hasMore = rows.Count > limit;
        return new AuditPageDto
        {
            Items = [.. page.Select(e => new AuditEntryDto
            {
                Id = e.Id,
                Action = e.Action,
                ActorId = e.ActorId,
                TargetUserId = e.TargetUserId,
                Data = e.DataJson is null ? null : JsonDocument.Parse(e.DataJson).RootElement.Clone(),
                CreatedAt = e.CreatedAt
            })],
            HasMore = hasMore,
            NextCursor = hasMore ? page[^1].Id.ToString() : null
        };
    }

    /// <summary>The caller is a member (403 otherwise, before anything about the chat is told) of a group (400 otherwise).</summary>
    private async Task<Chat> DemandGroupAsync(Guid chatId, Guid userId, CancellationToken ct)
    {
        await policy.DemandReadAsync(userId, chatId, ct);
        var chat = await chats.GetByIdAsync(chatId, ct) ?? throw new NotFoundException("Chat not found", "CHAT_NOT_FOUND");
        return chat.Type == ChatTypes.Group
            ? chat
            : throw new BadRequestException("This can only be done in a group", "NOT_A_GROUP");
    }

    private async Task<ChatMember> MemberAsync(Guid chatId, Guid userId, CancellationToken ct) =>
        await chats.GetMemberAsync(chatId, userId, ct)
        ?? throw new NotFoundException("The user is not a member of this group", "MEMBER_NOT_FOUND");

    private static GroupMemberDto WithoutAvatar(GroupMemberDto dto)
    {
        dto.AvatarId = null;
        return dto;
    }

    public static GroupMemberDto ToDto(ChatMember member) => new()
    {
        UserId = member.UserId,
        DisplayName = member.DisplayName,
        Username = member.Username,
        Role = member.Role,
        AvatarId = member.AvatarId,
        Permissions = GroupRights.Effective(member)
    };

    private static string NormalizeTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 and <= GroupOptions.MaxTitleLength
            ? trimmed
            : throw new BadRequestException(
                $"Group title must be 1 to {GroupOptions.MaxTitleLength} characters", "INVALID_TITLE");
    }

    /// <summary>
    /// Everyone may be added by this user (their <c>groupAdd</c> setting, blocks); otherwise nobody is
    /// added and the refusal names whom (D11).
    /// </summary>
    private async Task DemandMayAddAsync(Guid adderId, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        var refused = await policy.GetGroupAddRefusalsAsync(adderId, userIds, ct);
        if (refused.Count > 0)
            throw new PrivacyRestrictedException("These users do not allow you to add them to groups", refused);
    }

    /// <summary>Everyone to be added exists and is active; otherwise 404, as for a private chat.</summary>
    private async Task<IReadOnlyList<User>> DemandActiveUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return [];
        var found = await users.GetByIdsAsync(userIds, ct);
        if (found.Count(u => u.IsActive) != userIds.Count)
            throw new NotFoundException("User not found", "USER_NOT_FOUND");
        return found;
    }

    /// <summary>
    /// A system message by <paramref name="actorId"/>; <paramref name="recipients"/> get it as a new
    /// message (those who already see it on a new chat card are left out).
    /// </summary>
    private async Task<MessageDto> PostSystemMessageAsync(
        Guid chatId, Guid actorId, (MessageActionDto Action, string Text) system, DateTime now,
        IReadOnlyCollection<Guid> recipients, CancellationToken ct)
    {
        var created = MessageMapper.Map(await messages.CreateAsync(new Message
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = actorId,
            Type = MessageTypes.System,
            Text = system.Text,
            ContentJson = SystemMessages.Write(system.Action),
            CreatedAt = now
        }, ct: ct));

        if (recipients.Count > 0)
            await events.MessageCreatedAsync(created, recipients, ct);
        return created;
    }

    private Task AuditAsync(
        Guid chatId, Guid actorId, string action, Guid? targetUserId, object? data, DateTime now, CancellationToken ct) =>
        groups.AppendAuditAsync(new ChatAuditEntry
        {
            ChatId = chatId,
            ActorId = actorId,
            Action = action,
            TargetUserId = targetUserId,
            DataJson = data is null ? null : JsonSerializer.Serialize(data, Json),
            CreatedAt = now
        }, ct);

    private async Task<ChatListItemDto> CardAsync(Guid chatId, Guid userId, CancellationToken ct) =>
        ChatListItemMapper.Map(await chats.GetChatListItemAsync(chatId, userId, ct)
            ?? throw new NotFoundException("Chat not found", "CHAT_NOT_FOUND"));

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private BadRequestException TooManyMembers() =>
        new($"A group may have at most {_options.MaxMembers} members", "TOO_MANY_MEMBERS");
}
