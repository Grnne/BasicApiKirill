using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Options;

namespace BasicApi.Features.Messages;

/// <summary>Reactions to messages (D7): one per user per message, from the instance's set.</summary>
public interface IReactionService
{
    /// <summary>
    /// Sets the user's reaction, replacing their previous one; members get <c>ReactionsChanged</c>.
    /// The same reaction again changes nothing and sends nothing.
    /// Errors: 400 <c>INVALID_REACTION</c>, 403 <c>NOT_A_MEMBER</c>, 404 <c>MESSAGE_NOT_FOUND</c>.
    /// </summary>
    Task<MessageReactionsDto> SetAsync(Guid chatId, Guid userId, Guid messageId, string? emoji, CancellationToken ct = default);

    /// <summary>Removes the user's reaction; without one it is quiet. Errors as for setting.</summary>
    Task RemoveAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default);
}

public sealed class ReactionService(
    IDbSession db,
    IReactionRepository reactions,
    IMembershipService membership,
    IChatPolicy policy,
    IChatEventPublisher events,
    IOptions<MessageOptions> options) : IReactionService
{
    public const string InvalidReactionCode = "INVALID_REACTION";

    private const char VariationSelector = '️';

    public async Task<MessageReactionsDto> SetAsync(
        Guid chatId, Guid userId, Guid messageId, string? emoji, CancellationToken ct = default)
    {
        var allowed = Canonical(emoji) ?? throw new BadRequestException(
            "This reaction is not allowed; see the list in the documentation", InvalidReactionCode);
        return await ChangeAsync(chatId, userId, messageId, allowed, ct);
    }

    public Task RemoveAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default) =>
        ChangeAsync(chatId, userId, messageId, null, ct);

    private async Task<MessageReactionsDto> ChangeAsync(
        Guid chatId, Guid userId, Guid messageId, string? emoji, CancellationToken ct)
    {
        (await policy.CanReactAsync(userId, chatId, ct)).Demand();
        var memberIds = await membership.GetMemberIdsAsync(chatId, ct);

        return await db.InTransactionAsync(async ct =>
        {
            var change = emoji is null
                ? await reactions.RemoveAsync(chatId, messageId, userId, ct)
                : await reactions.SetAsync(chatId, messageId, userId, emoji, ct);
            if (!change.Found)
                throw new NotFoundException("Message not found in this chat", "MESSAGE_NOT_FOUND");

            var result = new MessageReactionsDto
            {
                ChatId = chatId,
                MessageId = messageId,
                Reactions = MessageMapper.Reactions(change.SummaryJson),
                UserId = userId,
                Emoji = emoji
            };
            if (change.Changed)
                await events.ReactionsChangedAsync(result, memberIds, ct);
            return result;
        }, ct: ct);
    }

    /// <summary>
    /// The allowed reaction as configured, or null. "❤" and "❤️" differ only by the emoji
    /// variation selector that keyboards add or drop; both mean the configured one.
    /// </summary>
    private string? Canonical(string? emoji) =>
        string.IsNullOrEmpty(emoji)
            ? null
            : options.Value.AllowedReactions.FirstOrDefault(a => Bare(a) == Bare(emoji));

    private static string Bare(string emoji) => emoji.Replace(VariationSelector.ToString(), string.Empty);
}
