using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IMessageRepository
{
    Task<IEnumerable<Message>> GetMessagesAsync(Guid chatId, DateTime? before, int limit);

    /// <summary>
    /// Retrieves messages using cursor-based pagination.
    /// Cursor encodes (CreatedAt, Id) — returns messages strictly older than the cursor.
    /// Results include one extra record to detect HasMore.
    /// </summary>
    Task<CursorResult<Message>> GetMessagesCursorAsync(Guid chatId, string? cursor, int limit);

    Task<Guid> CreateAsync(Message message);
    Task UpdateLastReadAsync(Guid chatId, Guid userId, Guid messageId);
    Task<int> GetUnreadCountAsync(Guid chatId, Guid userId);
}