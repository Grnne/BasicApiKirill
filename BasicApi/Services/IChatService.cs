using BasicApi.Models.Dto.Chat;

namespace BasicApi.Services;

public interface IChatService
{
    Task<List<ChatListItemDto>> GetUserChatsAsync(Guid userId, CancellationToken ct = default);
    Task<ChatDetailDto> GetChatDetailsAsync(Guid chatId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns one chat in the same shape as an entry of <see cref="GetUserChatsAsync"/>,
    /// resolved for the given viewer. Lets a client that only knows a chatId
    /// (e.g. after a push or a reconnect) render the chat without refetching the whole list.
    /// Throws NotFoundException when the chat does not exist and
    /// ForbiddenException when the caller is not a member.
    /// </summary>
    Task<ChatListItemDto> GetChatListItemAsync(Guid chatId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the private chat with another user, creating it if needed.
    /// A new chat is announced to the other user (<c>ChatCreated</c>, built from their
    /// point of view) and both learn each other's presence.
    /// Errors: 400 <c>SELF_CHAT</c>, 404 <c>USER_NOT_FOUND</c> (missing or deactivated).
    /// </summary>
    Task<PrivateChatResult> GetOrCreatePrivateChatAsync(Guid userId, Guid otherUserId, CancellationToken ct = default);

    /// <summary>
    /// The user's "Saved Messages" chat (type <c>saved</c>), created on first access. The user's
    /// other devices learn about a new one through sync, like about a chat they created.
    /// </summary>
    Task<PrivateChatResult> GetOrCreateSavedChatAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Searches user's chats by query.
    /// For type=group: searches by chat title (ILIKE).
    /// For type=private: searches by companion display name or username (ILIKE).
    /// If type is null/empty: searches both.
    /// </summary>
    /// <param name="userId">Current user ID.</param>
    /// <param name="query">Search query (min 1 character).</param>
    /// <param name="type">Optional filter: "group" or "private". Null/empty searches both.</param>
    /// <param name="limit">Max results.</param>
    /// <param name="ct">Cancellation.</param>
    Task<SearchChatsResponseDto> SearchChatsAsync(
        Guid userId, string query, string? type, int limit, CancellationToken ct = default);
}

/// <param name="Chat">The chat as seen by the caller.</param>
/// <param name="Created">True when the chat did not exist before this call.</param>
public sealed record PrivateChatResult(ChatListItemDto Chat, bool Created);
