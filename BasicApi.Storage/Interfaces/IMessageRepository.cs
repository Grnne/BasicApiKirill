using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IMessageRepository
{
    /// <summary>
    /// Retrieves messages with sender name via JOIN — avoids N+1.
    /// Uses cursor-based pagination.
    /// </summary>
    Task<CursorResult<MessageWithSender>> GetMessagesWithSenderCursorAsync(
        Guid chatId, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>
    /// Finds the oldest message strictly after the given moment (UTC), or null.
    /// "Jump to date" uses it as an exclusive cursor so the page ends at the date.
    /// </summary>
    Task<Message?> GetFirstMessageAfterDateAsync(Guid chatId, DateTime date, CancellationToken ct = default);

        /// <summary>
    /// Full-text search for messages within a chat using PostgreSQL tsvector.
    /// Supports cursor-based pagination with (created_at, id) composite cursor.
    /// Returns messages with sender names via JOIN — avoids N+1.
    /// </summary>
    /// <param name="chatId">Chat to search in.</param>
    /// <param name="query">Search query text.</param>
    /// <param name="cursor">Cursor from previous page (optional).</param>
    /// <param name="limit">Max results per page (default 20).</param>
    /// <returns>A tuple: the cursor result with messages, and the total count of matching messages.</returns>
    Task<(CursorResult<MessageWithSender> Result, int TotalCount)> SearchMessagesCursorAsync(
        Guid chatId, string query, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>Inserts a message; returns it with the sender's display name.</summary>
    Task<MessageWithSender> CreateAsync(Message message, CancellationToken ct = default);

    /// <summary>
    /// Moves the member's read pointer to the given message — forward only, by
    /// (created_at, id). A message from another chat (or a non-existent one) is
    /// rejected without touching the pointer.
    /// </summary>
    Task<ReadPointerUpdate> MarkReadAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default);
}