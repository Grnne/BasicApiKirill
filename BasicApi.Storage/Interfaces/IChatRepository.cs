using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IChatRepository
{
    Task<IEnumerable<Chat>> GetUserChatsAsync(Guid userId);
    Task<List<ChatListResult>> GetUserChatsBatchedAsync(Guid userId);
    Task<Chat?> GetByIdAsync(Guid chatId);
    Task<Chat?> GetPrivateChatAsync(Guid userId1, Guid userId2);
    Task<Guid> CreateAsync(Chat chat, Guid[] memberIds);
    Task<bool> IsMemberAsync(Guid chatId, Guid userId);
    Task<int> GetUnreadCountAsync(Guid chatId, Guid userId);
    Task<string?> GetCompanionNameAsync(Guid chatId, Guid userId);
    Task<string> GetUserNameAsync(Guid userId);
    Task<List<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId);
}