using System.Text.Json;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services.Events;
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
}

public sealed class GroupService(
    IDbSession db,
    IGroupRepository groups,
    IChatRepository chats,
    IUserRepository users,
    IMessageRepository messages,
    IPresenceService presence,
    IChatEventPublisher events,
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

        var chatId = Guid.NewGuid();
        var now = Now();
        var card = await db.InTransactionAsync(async ct =>
        {
            await groups.CreateAsync(chatId, name, creatorId, invited, now, ct);

            // Not announced as a new message: the card of the new chat already shows it, and the
            // current client would count it as one more unread on top of the card.
            await PostSystemMessageAsync(chatId, creatorId, SystemMessages.Created(name), now, recipients: [], ct);
            await AuditAsync(chatId, creatorId, "group_created", null, new { title = name, memberIds = invited }, now, ct);

            var own = await CardAsync(chatId, creatorId, ct);
            if (invited.Count > 0)
                await events.ChatCreatedAsync(invited, await CardAsync(chatId, invited[0], ct), ct);
            await events.ChatCreatedAsync(creatorId, own, live: false, ct);
            return own;
        }, ct: ct);

        await presence.IntroduceAsync([creatorId, .. invited], [], CancellationToken.None);
        return card;
    }

    private static string NormalizeTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 and <= GroupOptions.MaxTitleLength
            ? trimmed
            : throw new BadRequestException(
                $"Group title must be 1 to {GroupOptions.MaxTitleLength} characters", "INVALID_TITLE");
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
    private async Task PostSystemMessageAsync(
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
