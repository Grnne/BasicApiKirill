using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IMessageRepository
{
    Task<IEnumerable<Message>> GetMessagesAsync(Guid chatId, DateTime? before, int limit);
    Task<Guid> CreateAsync(Message message);
    Task UpdateLastReadAsync(Guid chatId, Guid userId, Guid messageId);
    Task<int> GetUnreadCountAsync(Guid chatId, Guid userId);
}