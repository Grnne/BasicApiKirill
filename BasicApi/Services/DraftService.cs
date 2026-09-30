using BasicApi.Middleware.Exceptions;
using BasicApi.Models;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

/// <summary>
/// Drafts (D4): one per user and chat, the same on all the user's devices. The client saves with
/// a delay while typing; the other devices get <c>DraftUpdated</c>. Sending a message clears it.
/// </summary>
public interface IDraftService
{
    /// <summary>
    /// Saves the draft and tells the user's devices; the same draft again changes nothing and
    /// sends nothing. Text of only whitespace without a reply removes the draft (returns null).
    /// Errors: 400 <c>MESSAGE_TOO_LONG</c>/<c>INVALID_ENTITIES</c>/<c>REPLY_TARGET_NOT_FOUND</c>,
    /// 403 <c>NOT_A_MEMBER</c>.
    /// </summary>
    Task<DraftDto?> SaveAsync(
        Guid chatId, Guid userId, string? text, IReadOnlyList<MessageEntityDto>? entities, Guid? replyToMessageId,
        CancellationToken ct = default);

    /// <summary>Removes the draft; without one it is quiet. Errors: 403 <c>NOT_A_MEMBER</c>.</summary>
    Task DeleteAsync(Guid chatId, Guid userId, CancellationToken ct = default);
}

public sealed class DraftService(
    IDbSession db,
    IDraftRepository drafts,
    IMessageRepository messages,
    IMembershipService membership,
    IChatPolicy policy,
    IChatEventPublisher events,
    TimeProvider? time = null) : IDraftService
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<DraftDto?> SaveAsync(
        Guid chatId, Guid userId, string? text, IReadOnlyList<MessageEntityDto>? entities, Guid? replyToMessageId,
        CancellationToken ct = default)
    {
        text ??= string.Empty;
        if (text.Length > MessageText.MaxLength)
            throw new BadRequestException($"Draft text is longer than {MessageText.MaxLength} characters", MessageText.TooLongCode);
        if (MessageEntities.Validate(entities, text, out var formatting) is { } error)
            throw new BadRequestException(error, MessageEntities.InvalidCode);

        await policy.DemandReadAsync(userId, chatId, ct);

        if (string.IsNullOrWhiteSpace(text) && replyToMessageId is null)
        {
            await DeleteOwnAsync(chatId, userId, ct);
            return null;
        }

        var mentioned = MessageEntities.MentionedUsers(formatting);
        if (mentioned.Count > 0)
        {
            var memberIds = await membership.GetMemberIdsAsync(chatId, ct);
            if (mentioned.Any(id => !memberIds.Contains(id)))
                throw new BadRequestException("A mentioned user is not a member of this chat", MessageEntities.InvalidCode);
        }

        if (replyToMessageId is { } replyId &&
            await messages.GetAsync(chatId, replyId, ct) is not { DeletedAt: null, Type: not MessageTypes.System })
        {
            throw new BadRequestException("The message to reply to is not in this chat", "REPLY_TARGET_NOT_FOUND");
        }

        var entitiesJson = MessageEntities.Serialize(formatting);
        return await db.InTransactionAsync(async ct =>
        {
            // Typing pauses often send what is already saved: no write, no event.
            var existing = await drafts.GetAsync(userId, chatId, ct);
            if (existing is not null && existing.Text == text && existing.ReplyToMessageId == replyToMessageId &&
                MessageEntities.Deserialize(existing.EntitiesJson).SequenceEqual(formatting))
            {
                return Map(existing);
            }

            var draft = new Draft
            {
                UserId = userId,
                ChatId = chatId,
                Text = text,
                EntitiesJson = entitiesJson,
                ReplyToMessageId = replyToMessageId,
                UpdatedAt = _time.GetUtcNow().UtcDateTime
            };
            await drafts.SaveAsync(draft, ct);

            var dto = Map(draft);
            await events.DraftUpdatedAsync(new DraftUpdatedDto { ChatId = chatId, Draft = dto }, userId, ct);
            return dto;
        }, ct: ct);
    }

    public async Task DeleteAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        await policy.DemandReadAsync(userId, chatId, ct);
        await DeleteOwnAsync(chatId, userId, ct);
    }

    private Task DeleteOwnAsync(Guid chatId, Guid userId, CancellationToken ct) =>
        db.InTransactionAsync(async ct =>
        {
            if (await drafts.DeleteAsync(userId, chatId, ct))
                await events.DraftUpdatedAsync(new DraftUpdatedDto { ChatId = chatId, Draft = null }, userId, ct);
            return true;
        }, ct: ct);

    public static DraftDto Map(Draft draft) => new()
    {
        Text = draft.Text,
        Entities = MessageEntities.Deserialize(draft.EntitiesJson),
        ReplyToMessageId = draft.ReplyToMessageId,
        UpdatedAt = draft.UpdatedAt
    };
}
