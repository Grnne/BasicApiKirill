using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IMessageRepository
{
    /// <summary>
    /// Retrieves messages using cursor-based pagination.
    /// Cursor encodes (CreatedAt, Id) — returns messages strictly older than the cursor.
    /// Results include one extra record to detect HasMore.
    /// </summary>
    Task<CursorResult<Message>> GetMessagesCursorAsync(Guid chatId, string? cursor, int limit);

    /// <summary>
    /// Retrieves messages with sender name via JOIN — avoids N+1.
    /// Uses cursor-based pagination.
    /// </summary>
    Task<CursorResult<MessageWithSender>> GetMessagesWithSenderCursorAsync(Guid chatId, string? cursor, int limit);

    /// <summary>
    /// Finds the most recent message at or before the given date.
    /// Used by the "jump to date" endpoint to build a cursor targeting a specific time.
    /// Returns null if no messages exist before that date.
    /// </summary>
    Task<Message?> GetFirstMessageBeforeDateAsync(Guid chatId, DateTime date);

    Task<Guid> CreateAsync(Message message);
    Task UpdateLastReadAsync(Guid chatId, Guid userId, Guid messageId);
    Task<int> GetUnreadCountAsync(Guid chatId, Guid userId);
}