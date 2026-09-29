using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Services.Events;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

public sealed class ChatService(
    IChatRepository chatRepository,
    IUserRepository userRepository,
    IMembershipService membership,
    IPresenceService presence,
    IChatEventPublisher events) : IChatService
{
    public async Task<List<ChatListItemDto>> GetUserChatsAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await chatRepository.GetUserChatsBatchedAsync(userId, ct);
        return [.. rows.Select(ChatListItemMapper.Map)];
    }

    public async Task<ChatListItemDto> GetChatListItemAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        // Same 404/403 semantics as GetChatDetailsAsync — the projection query
        // itself cannot tell "chat missing" from "not a member".
        _ = await chatRepository.GetByIdAsync(chatId, ct)
            ?? throw ChatNotFound();

        await membership.EnsureMemberAsync(chatId, userId, ct);

        var row = await chatRepository.GetChatListItemAsync(chatId, userId, ct)
            ?? throw ChatNotFound();

        return ChatListItemMapper.Map(row);
    }

    public async Task<ChatDetailDto> GetChatDetailsAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        var chat = await chatRepository.GetByIdAsync(chatId, ct)
            ?? throw ChatNotFound();

        await membership.EnsureMemberAsync(chatId, userId, ct);

        var participants = await chatRepository.GetChatParticipantsAsync(chatId, ct);

        return new ChatDetailDto
        {
            ChatId = chat.Id,
            Type = chat.Type,
            Title = chat.Title,
            Participants = [.. participants.Select(p => new ChatParticipantDto
            {
                UserId = p.UserId,
                DisplayName = p.DisplayName,
                Username = p.Username
            })]
        };
    }

    public async Task<PrivateChatResult> GetOrCreatePrivateChatAsync(
        Guid userId, Guid otherUserId, CancellationToken ct = default)
    {
        if (userId == otherUserId)
            throw new BadRequestException("Cannot create chat with yourself", "SELF_CHAT");

        // Без этой проверки несуществующий собеседник ронял вставку на внешнем ключе (500).
        // Деактивированный — тоже 404: писать ему некому.
        var other = await userRepository.GetByIdAsync(otherUserId, ct);
        if (other is null || !other.IsActive)
            throw new NotFoundException("User not found", "USER_NOT_FOUND");

        var (chatId, created) = await chatRepository.GetOrCreatePrivateChatAsync(userId, otherUserId, ct);

        if (created)
        {
            // Чат уже создан — сообщаем о нём, даже если клиент ушёл.
            // Карточка для второго участника своя: собеседник в ней — создатель чата.
            var recipientRow = await chatRepository.GetChatListItemAsync(chatId, otherUserId, CancellationToken.None);
            if (recipientRow is not null)
                await events.ChatCreatedAsync(otherUserId, ChatListItemMapper.Map(recipientRow), CancellationToken.None);

            await presence.IntroduceAsync(userId, otherUserId, CancellationToken.None);
        }

        var row = await chatRepository.GetChatListItemAsync(chatId, userId, ct)
            ?? throw ChatNotFound();

        return new PrivateChatResult(ChatListItemMapper.Map(row), created);
    }

    public async Task<SearchChatsResponseDto> SearchChatsAsync(
        Guid userId, string query, string? type, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new BadRequestException("Query cannot be empty", "INVALID_QUERY");

        var typeFilter = type?.ToLowerInvariant();

        // Вне транзакции каждый запрос берёт своё соединение — можно параллельно.
        var rowsTask = chatRepository.SearchChatsBatchedAsync(userId, query, typeFilter, limit, ct);
        var countTask = chatRepository.CountChatsByQueryAsync(userId, query, typeFilter, ct);

        await Task.WhenAll(rowsTask, countTask);

        return new SearchChatsResponseDto
        {
            Items = [.. rowsTask.Result.Select(ChatListItemMapper.Map)],
            Query = query,
            TotalCount = countTask.Result
        };
    }

    private static NotFoundException ChatNotFound() => new("Chat not found", "CHAT_NOT_FOUND");
}
