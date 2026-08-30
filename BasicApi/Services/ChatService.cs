using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Storage.Interfaces;
namespace BasicApi.Services;

public class ChatService(IChatRepository chatRepository, IMessageRepository messageRepository) : IChatService
{
    public async Task<List<ChatListItemDto>> GetUserChatsAsync(Guid userId)
    {
        // Single batched query replaces the previous N+1 pattern
        var rows = await chatRepository.GetUserChatsBatchedAsync(userId);

        return [.. rows.Select(ChatListItemMapper.Map)];
    }

    public async Task<ChatListItemDto> GetChatListItemAsync(Guid chatId, Guid userId)
    {
        // Same 404/403 semantics as GetChatDetailsAsync — the projection query
        // itself cannot tell "chat missing" from "not a member".
        _ = await chatRepository.GetByIdAsync(chatId)
            ?? throw new NotFoundException("Chat not found", "CHAT_NOT_FOUND");

        var isMember = await chatRepository.IsMemberAsync(chatId, userId);
        if (!isMember)
            throw new ForbiddenException("User is not a member of this chat", "NOT_A_MEMBER");

        var row = await chatRepository.GetChatListItemAsync(chatId, userId)
            ?? throw new NotFoundException("Chat not found", "CHAT_NOT_FOUND");

        return ChatListItemMapper.Map(row);
    }

        public async Task<ChatDetailDto> GetChatDetailsAsync(Guid chatId, Guid userId)
    {
        var chat = await chatRepository.GetByIdAsync(chatId)
            ?? throw new NotFoundException("Chat not found", "CHAT_NOT_FOUND");

        // Authorization check — caller must be a member
        var isMember = await chatRepository.IsMemberAsync(chatId, userId);
        if (!isMember)
            throw new ForbiddenException("User is not a member of this chat", "NOT_A_MEMBER");

        var participants = await chatRepository.GetChatParticipantsAsync(chatId);

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

    public async Task<CursorPaginatedResponse<MessageDto>> GetChatMessagesCursorAsync(
        Guid chatId, Guid userId, string? cursor, int limit)
    {
        // Authorization check — caller must be a member
        var isMember = await chatRepository.IsMemberAsync(chatId, userId);
        if (!isMember)
            throw new ForbiddenException("User is not a member of this chat", "NOT_A_MEMBER");

        // Fetch page from storage (cursor-based) with sender names via JOIN
        var result = await messageRepository.GetMessagesWithSenderCursorAsync(chatId, cursor, limit);

        // Map entities to DTOs
        var messages = result.Items.Select(m => new MessageDto
        {
            Id = m.Id,
            ChatId = chatId,
            SenderId = m.SenderId,
            SenderName = m.SenderName,
            Text = m.Text,
            CreatedAt = m.CreatedAt,
            IsRead = false // TODO: resolve actual read status
        }).ToList();
        // Build next cursor from the last message in the page
        string? nextCursor = null;
        if (messages.Count > 0)
        {
            var last = messages[^1];
            nextCursor = new Storage.Dto.CursorDto(last.CreatedAt, last.Id).Encode();
        }

                return new CursorPaginatedResponse<MessageDto>
        {
            Items = [.. messages.OrderBy(m => m.CreatedAt)],
            NextCursor = nextCursor,
            HasMore = result.HasMore
        };
    }

    public async Task<SearchMessagesResponseDto> SearchChatMessagesCursorAsync(
        Guid chatId, Guid userId, string query, string? cursor, int limit)
    {
        // Validate query
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            throw new BadRequestException("Query must be at least 2 characters long", "INVALID_QUERY");

        // Authorization check — caller must be a member
        var isMember = await chatRepository.IsMemberAsync(chatId, userId);
        if (!isMember)
            throw new ForbiddenException("User is not a member of this chat", "NOT_A_MEMBER");

                // Fetch page from storage (cursor-based) with full-text search + sender names
        var (result, totalCount) = await messageRepository.SearchMessagesCursorAsync(chatId, query, cursor, limit);

        // Map entities to DTOs
        var messages = result.Items.Select(m => new MessageDto
        {
            Id = m.Id,
            ChatId = chatId,
            SenderId = m.SenderId,
            SenderName = m.SenderName,
            Text = m.Text,
            CreatedAt = m.CreatedAt,
            IsRead = false // TODO: resolve actual read status
        }).ToList();

        // Build next cursor from the last message in the page
        string? nextCursor = null;
        if (messages.Count > 0)
        {
            var last = messages[^1];
            nextCursor = new Storage.Dto.CursorDto(last.CreatedAt, last.Id).Encode();
        }

                return new SearchMessagesResponseDto
        {
            Items = [.. messages.OrderBy(m => m.CreatedAt)],
            NextCursor = nextCursor,
            HasMore = result.HasMore,
            Query = query,
            TotalCount = totalCount
        };
    }

        public async Task<SearchChatsResponseDto> SearchChatsAsync(Guid userId, string query, string? type, int limit)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new BadRequestException("Query cannot be empty", "INVALID_QUERY");

        var typeFilter = type?.ToLowerInvariant();

        // Single unified query — branching by type is now inside the repository
        var rowsTask = chatRepository.SearchChatsBatchedAsync(userId, query, typeFilter, limit);
        var countTask = chatRepository.CountChatsByQueryAsync(userId, query, typeFilter);

        await Task.WhenAll(rowsTask, countTask);

        return new SearchChatsResponseDto
        {
            Items = [.. rowsTask.Result.Select(ChatListItemMapper.Map)],
            Query = query,
            TotalCount = countTask.Result
        };
    }
}

