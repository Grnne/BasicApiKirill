using BasicApi.Middleware;
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

        return [.. rows.Select(r => new ChatListItemDto
        {
            ChatId = r.ChatId,
            Type = r.Type,
            Title = r.Title,
            CompanionName = r.CompanionName,
            UnreadCount = r.UnreadCount,
            LastActivityAt = r.LastMessageCreatedAt ?? r.CreatedAt,
            LastMessage = r.LastMessageId is not null ? new MessageDto
            {
                Id = r.LastMessageId!.Value,
                SenderId = r.LastMessageSenderId!.Value,
                SenderName = r.LastMessageSenderName ?? "Unknown",
                Text = r.LastMessageText ?? string.Empty,
                CreatedAt = r.LastMessageCreatedAt!.Value,
                IsRead = false
            } : null
        })];
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

    public async Task<CursorPaginatedResponse<MessageDto>> GetChatMessagesCursorAsync(
        Guid chatId, Guid userId, string? cursor, int limit)
    {
        // Authorization check — caller must be a member
        var isMember = await chatRepository.IsMemberAsync(chatId, userId);
        if (!isMember)
            throw new UnauthorizedAccessException("User is not a member of this chat");

        // Fetch page from storage (cursor-based) with sender names via JOIN
        var result = await messageRepository.GetMessagesWithSenderCursorAsync(chatId, cursor, limit);

        // Map entities to DTOs
        var messages = result.Items.Select(m => new MessageDto
        {
            Id = m.Id,
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
}

