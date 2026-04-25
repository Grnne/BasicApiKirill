using BasicApi.Middleware;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Storage.Interfaces;
namespace BasicApi.Services;

public class ChatService(IChatRepository chatRepository, IMessageRepository messageRepository) : IChatService
{
    public async Task<List<ChatListItemDto>> GetUserChatsAsync(Guid userId)
    {
        var chats = await chatRepository.GetUserChatsAsync(userId);
        var result = new List<ChatListItemDto>();

        foreach (var chat in chats)
        {
            // Get the last message using cursor-based approach for efficiency
            var lastMessageResult = await messageRepository.GetMessagesCursorAsync(chat.Id, null, 1);
            var lastMessage = lastMessageResult.Items.FirstOrDefault();

            // Get unread count
            var unreadCount = await messageRepository.GetUnreadCountAsync(chat.Id, userId);

            // For private chats, get companion name
            string? companionName = null;
            if (chat.Type == "private")
            {
                companionName = await chatRepository.GetCompanionNameAsync(chat.Id, userId);
            }

            result.Add(new ChatListItemDto
            {
                ChatId = chat.Id,
                Type = chat.Type,
                Title = chat.Title,
                CompanionName = companionName,
                LastMessage = lastMessage != null ? new MessageDto
                {
                    Id = lastMessage.Id,
                    SenderId = lastMessage.SenderId,
                    SenderName = await chatRepository.GetUserNameAsync(lastMessage.SenderId),
                    Text = lastMessage.Text,
                    CreatedAt = lastMessage.CreatedAt,
                    IsRead = false // TODO: resolve actual read status
                } : null,
                UnreadCount = unreadCount,
                LastActivityAt = lastMessage?.CreatedAt ?? chat.CreatedAt
            });
        }

        return [.. result.OrderByDescending(c => c.LastActivityAt)];
    }

    public async Task<ChatDetailDto> GetChatDetailsAsync(Guid chatId, Guid userId)
    {
        var chat = await chatRepository.GetByIdAsync(chatId)
            ?? throw new NotFoundException("Chat not found");
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

    public async Task<List<MessageDto>> GetChatMessagesAsync(Guid chatId, Guid userId, DateTime? before, int limit)
    {
        var isMember = await chatRepository.IsMemberAsync(chatId, userId);
        if (!isMember)
            throw new UnauthorizedAccessException("User is not a member of this chat");

        var messages = await messageRepository.GetMessagesAsync(chatId, before, limit);
        var result = new List<MessageDto>();

        foreach (var message in messages)
        {
            result.Add(new MessageDto
            {
                Id = message.Id,
                SenderId = message.SenderId,
                SenderName = await chatRepository.GetUserNameAsync(message.SenderId),
                Text = message.Text,
                CreatedAt = message.CreatedAt,
                IsRead = false // TODO: resolve actual read status
            });
        }

        return [.. result.OrderBy(m => m.CreatedAt)];
    }

    public async Task<CursorPaginatedResponse<MessageDto>> GetChatMessagesCursorAsync(
        Guid chatId, Guid userId, string? cursor, int limit)
    {
        // Authorization check — caller must be a member
        var isMember = await chatRepository.IsMemberAsync(chatId, userId);
        if (!isMember)
            throw new UnauthorizedAccessException("User is not a member of this chat");

        // Fetch page from storage (cursor-based)
        var result = await messageRepository.GetMessagesCursorAsync(chatId, cursor, limit);

        // Map entities to DTOs
        var messages = new List<MessageDto>();
        foreach (var message in result.Items)
        {
            messages.Add(new MessageDto
            {
                Id = message.Id,
                SenderId = message.SenderId,
                SenderName = await chatRepository.GetUserNameAsync(message.SenderId),
                Text = message.Text,
                CreatedAt = message.CreatedAt,
                IsRead = false // TODO: resolve actual read status
            });
        }

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
}

