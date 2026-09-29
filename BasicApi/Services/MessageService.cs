using BasicApi.Middleware.Exceptions;
using BasicApi.Models;
using BasicApi.Models.Dto.Message;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Exceptions;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

public interface IMessageService
{
    /// <summary>Страница истории: от новых к старым по курсору, внутри страницы — по порядку (seq).</summary>
    Task<CursorPaginatedResponse<MessageDto>> GetPageAsync(
        Guid chatId, Guid userId, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>
    /// «Переход к дате»: страница, которая заканчивается последним сообщением
    /// в этот момент или раньше; дальше в прошлое — по <c>nextCursor</c>.
    /// </summary>
    Task<CursorPaginatedResponse<MessageDto>> GetPageAtAsync(
        Guid chatId, Guid userId, DateTime date, int limit, CancellationToken ct = default);

    /// <summary>Полнотекстовый поиск в чате с курсорной пагинацией.</summary>
    Task<SearchMessagesResponseDto> SearchAsync(
        Guid chatId, Guid userId, string query, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>
    /// Отправка: текст обрезается по краям и проверяется; участникам уходит событие.
    /// С <paramref name="clientMessageId"/> отправка идемпотентна: повтор возвращает уже
    /// созданное сообщение (<c>Created = false</c>) и ничего не рассылает.
    /// Ошибки: 400 <c>MESSAGE_EMPTY</c>/<c>MESSAGE_TOO_LONG</c>, 403 <c>NOT_A_MEMBER</c>,
    /// 409 <c>CLIENT_MESSAGE_ID_CONFLICT</c> — этот id уже занят сообщением в другом чате.
    /// </summary>
    Task<SendResult> SendAsync(
        Guid chatId, Guid senderId, string? text, Guid? clientMessageId = null, CancellationToken ct = default);

    /// <summary>
    /// Двигает указатель прочитанного вперёд. Сообщение не из этого чата — 404
    /// <c>MESSAGE_NOT_FOUND</c>; попытка отмотать назад — не ошибка, ничего не меняется.
    /// </summary>
    Task MarkReadAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default);
}

/// <param name="Message">Сообщение — новое или найденное по clientMessageId.</param>
/// <param name="Created">false — это повтор уже выполненной отправки.</param>
public sealed record SendResult(MessageDto Message, bool Created);

public sealed class MessageService(
    IDbSession db,
    IMessageRepository messageRepository,
    IMembershipService membership,
    IChatPolicy policy,
    IChatEventPublisher events) : IMessageService
{
    public async Task<CursorPaginatedResponse<MessageDto>> GetPageAsync(
        Guid chatId, Guid userId, string? cursor, int limit, CancellationToken ct = default)
    {
        var parsed = ParseCursor(cursor);
        await policy.DemandReadAsync(userId, chatId, ct);

        return await PageAsync(chatId, await ResolveCursorAsync(chatId, parsed, ct), limit, ct);
    }

    public async Task<CursorPaginatedResponse<MessageDto>> GetPageAtAsync(
        Guid chatId, Guid userId, DateTime date, int limit, CancellationToken ct = default)
    {
        // Доступ — до любых запросов к сообщениям чата.
        await policy.DemandReadAsync(userId, chatId, ct);

        // Дата с любым смещением (…Z, …+03:00) — один и тот же момент; в базе — UTC.
        var utcDate = date.Kind switch
        {
            DateTimeKind.Local => date.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(date, DateTimeKind.Utc),
            _ => date
        };

        // Курсор исключающий, поэтому берём первое сообщение ПОСЛЕ даты: страница перед
        // ним заканчивается последним сообщением до даты включительно. Сообщений после
        // даты нет — это просто последняя страница.
        var firstAfter = await messageRepository.GetFirstSeqAfterAsync(chatId, utcDate, ct);
        return await PageAsync(chatId, firstAfter, limit, ct);
    }

    private async Task<CursorPaginatedResponse<MessageDto>> PageAsync(
        Guid chatId, long? beforeSeq, int limit, CancellationToken ct)
    {
        var result = await messageRepository.GetMessagesWithSenderCursorAsync(chatId, beforeSeq, limit, ct);
        var messages = result.Items.Select(Map).ToList();

        return new CursorPaginatedResponse<MessageDto>
        {
            Items = [.. messages.OrderBy(m => m.Seq)],
            NextCursor = NextCursor(messages, result.HasMore),
            HasMore = result.HasMore
        };
    }

    public async Task<SearchMessagesResponseDto> SearchAsync(
        Guid chatId, Guid userId, string query, string? cursor, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            throw new BadRequestException("Query must be at least 2 characters long", "INVALID_QUERY");

        var parsed = ParseCursor(cursor);
        await policy.DemandReadAsync(userId, chatId, ct);

        var beforeSeq = await ResolveCursorAsync(chatId, parsed, ct);
        var (result, totalCount) = await messageRepository.SearchMessagesCursorAsync(chatId, query, beforeSeq, limit, ct);
        var messages = result.Items.Select(Map).ToList();

        return new SearchMessagesResponseDto
        {
            Items = [.. messages.OrderBy(m => m.Seq)],
            NextCursor = NextCursor(messages, result.HasMore),
            HasMore = result.HasMore,
            Query = query,
            TotalCount = totalCount
        };
    }

    public async Task<SendResult> SendAsync(
        Guid chatId, Guid senderId, string? text, Guid? clientMessageId = null, CancellationToken ct = default)
    {
        // Текст проверяем до похода в базу: это бесплатно.
        var textError = MessageText.Normalize(text, out var normalized);
        if (textError == MessageText.EmptyCode)
            throw new BadRequestException("Message text is empty", textError);
        if (textError == MessageText.TooLongCode)
            throw new BadRequestException($"Message text is longer than {MessageText.MaxLength} characters", textError);

        await policy.DemandPostAsync(senderId, chatId, ct);

        // Повтор отправки (ретрай после обрыва сети) — то же сообщение, без второго события.
        if (clientMessageId is { } retryId &&
            await messageRepository.GetByClientMessageIdAsync(senderId, retryId, ct) is { } sent)
        {
            return AlreadySent(sent, chatId);
        }

        var memberIds = await membership.GetMemberIdsAsync(chatId, ct);

        try
        {
            // Сообщение и событие о нём — одна транзакция: сохранено одно — сохранено и другое.
            var message = await db.InTransactionAsync(async ct =>
            {
                var created = Map(await messageRepository.CreateAsync(new Message
                {
                    Id = Guid.NewGuid(),
                    ChatId = chatId,
                    SenderId = senderId,
                    Text = normalized,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                }, clientMessageId, ct));

                await events.MessageCreatedAsync(created, memberIds, ct);
                return created;
            }, ct: ct);

            return new SendResult(message, Created: true);
        }
        catch (DuplicateKeyException)
        {
            // Параллельный повтор той же отправки успел раньше — отдаём его результат.
            var winner = await messageRepository.GetByClientMessageIdAsync(senderId, clientMessageId!.Value, CancellationToken.None)
                ?? throw new InvalidOperationException("Duplicate clientMessageId, but the message is not found");
            return AlreadySent(winner, chatId);
        }
    }

    private static SendResult AlreadySent(MessageWithSender sent, Guid chatId) =>
        sent.ChatId == chatId
            ? new SendResult(Map(sent), Created: false)
            : throw new ConflictException(
                "clientMessageId is already used by a message in another chat", "CLIENT_MESSAGE_ID_CONFLICT");

    public async Task MarkReadAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default)
    {
        await policy.DemandReadAsync(userId, chatId, ct);

        // Сообщение из чужого чата — 404: иначе указатель мог бы встать на чужое
        // сообщение, и счётчик непрочитанных сломался бы.
        var update = await messageRepository.MarkReadAsync(chatId, userId, messageId, ct);
        if (update == ReadPointerUpdate.MessageNotFound)
            throw new NotFoundException("Message not found in this chat", "MESSAGE_NOT_FOUND");
    }

    private static MessageDto Map(MessageWithSender m) => new()
    {
        Id = m.Id,
        ChatId = m.ChatId,
        SenderId = m.SenderId,
        SenderName = m.SenderName,
        Text = m.Text,
        CreatedAt = m.CreatedAt,
        IsRead = false, // TODO: статус прочтения собеседником — план 2
        Seq = m.Seq,
        ClientMessageId = m.ClientMessageId
    };

    /// <summary>
    /// Курсор на следующую (более старую) страницу — от последнего сообщения страницы
    /// (страница пришла от новых к старым). Нет следующей страницы — нет и курсора.
    /// </summary>
    private static string? NextCursor(List<MessageDto> newestFirst, bool hasMore) =>
        hasMore && newestFirst.Count > 0 ? MessageCursor.BeforeSeqOf(newestFirst[^1].Seq).Encode() : null;

    /// <summary>Формат курсора проверяется до всего остального: битый курсор — 400.</summary>
    private static MessageCursor? ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return null;
        return MessageCursor.TryDecode(cursor, out var parsed) ? parsed : throw InvalidCursor();
    }

    /// <summary>Курсор старого формата указывает на сообщение по id — берём его seq.</summary>
    private async Task<long?> ResolveCursorAsync(Guid chatId, MessageCursor? cursor, CancellationToken ct)
    {
        if (cursor is not { } c)
            return null;
        if (c.BeforeSeq is { } seq)
            return seq;
        return await messageRepository.GetSeqAsync(chatId, c.LegacyMessageId!.Value, ct) ?? throw InvalidCursor();
    }

    private static BadRequestException InvalidCursor() => new("Cursor is malformed", "INVALID_CURSOR");
}
