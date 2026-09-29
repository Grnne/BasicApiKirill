using BasicApi.Middleware.Exceptions;
using BasicApi.Models;
using BasicApi.Models.Dto.Message;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

public interface IMessageService
{
    /// <summary>Страница истории: от новых к старым по курсору, внутри страницы — по времени.</summary>
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
    /// Ошибки: 400 <c>MESSAGE_EMPTY</c>/<c>MESSAGE_TOO_LONG</c>, 403 <c>NOT_A_MEMBER</c>.
    /// </summary>
    Task<MessageDto> SendAsync(Guid chatId, Guid senderId, string? text, CancellationToken ct = default);

    /// <summary>
    /// Двигает указатель прочитанного вперёд. Сообщение не из этого чата — 404
    /// <c>MESSAGE_NOT_FOUND</c>; попытка отмотать назад — не ошибка, ничего не меняется.
    /// </summary>
    Task MarkReadAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default);
}

public sealed class MessageService(
    IMessageRepository messageRepository,
    IMembershipService membership,
    IChatPolicy policy,
    IChatEventPublisher events) : IMessageService
{
    public async Task<CursorPaginatedResponse<MessageDto>> GetPageAsync(
        Guid chatId, Guid userId, string? cursor, int limit, CancellationToken ct = default)
    {
        EnsureValidCursor(cursor);
        await policy.DemandReadAsync(userId, chatId, ct);

        var result = await messageRepository.GetMessagesWithSenderCursorAsync(chatId, cursor, limit, ct);
        var messages = result.Items.Select(Map).ToList();

        return new CursorPaginatedResponse<MessageDto>
        {
            Items = [.. messages.OrderBy(m => m.CreatedAt)],
            NextCursor = NextCursor(messages, result.HasMore),
            HasMore = result.HasMore
        };
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

        // Курсор исключающий, поэтому строим его от первого сообщения ПОСЛЕ даты:
        // страница перед ним заканчивается последним сообщением до даты включительно.
        // Сообщений после даты нет — это просто последняя страница (cursor = null).
        var firstAfter = await messageRepository.GetFirstMessageAfterDateAsync(chatId, utcDate, ct);
        var cursor = firstAfter is not null
            ? new CursorDto(firstAfter.CreatedAt, firstAfter.Id).Encode()
            : null;

        return await GetPageAsync(chatId, userId, cursor, limit, ct);
    }

    public async Task<SearchMessagesResponseDto> SearchAsync(
        Guid chatId, Guid userId, string query, string? cursor, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            throw new BadRequestException("Query must be at least 2 characters long", "INVALID_QUERY");

        EnsureValidCursor(cursor);
        await policy.DemandReadAsync(userId, chatId, ct);

        var (result, totalCount) = await messageRepository.SearchMessagesCursorAsync(chatId, query, cursor, limit, ct);
        var messages = result.Items.Select(Map).ToList();

        return new SearchMessagesResponseDto
        {
            Items = [.. messages.OrderBy(m => m.CreatedAt)],
            NextCursor = NextCursor(messages, result.HasMore),
            HasMore = result.HasMore,
            Query = query,
            TotalCount = totalCount
        };
    }

    public async Task<MessageDto> SendAsync(Guid chatId, Guid senderId, string? text, CancellationToken ct = default)
    {
        // Текст проверяем до похода в базу: это бесплатно.
        var textError = MessageText.Normalize(text, out var normalized);
        if (textError == MessageText.EmptyCode)
            throw new BadRequestException("Message text is empty", textError);
        if (textError == MessageText.TooLongCode)
            throw new BadRequestException($"Message text is longer than {MessageText.MaxLength} characters", textError);

        await policy.DemandPostAsync(senderId, chatId, ct);
        var memberIds = await membership.GetMemberIdsAsync(chatId, ct);

        var created = await messageRepository.CreateAsync(new Message
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = senderId,
            Text = normalized,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        }, ct);

        var message = Map(created);

        // Сообщение уже сохранено — событие отправляем, даже если клиент ушёл.
        await events.MessageCreatedAsync(message, memberIds, CancellationToken.None);
        return message;
    }

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
        IsRead = false // TODO: статус прочтения собеседником — план 2
    };

    /// <summary>
    /// Курсор на следующую (более старую) страницу — от последнего сообщения страницы
    /// (страница пришла от новых к старым). Нет следующей страницы — нет и курсора.
    /// </summary>
    private static string? NextCursor(List<MessageDto> newestFirst, bool hasMore) =>
        hasMore && newestFirst.Count > 0
            ? new CursorDto(newestFirst[^1].CreatedAt, newestFirst[^1].Id).Encode()
            : null;

    private static void EnsureValidCursor(string? cursor)
    {
        if (!string.IsNullOrEmpty(cursor) && !CursorDto.TryDecode(cursor, out _))
            throw new BadRequestException("Cursor is malformed", "INVALID_CURSOR");
    }
}
