# Cursor-Based Pagination Refactoring — Summary

## What was done

Production-grade cursor-based pagination for the `GetChatAsync` (messages) endpoint, plus a bonus fix for the broken `GetUnreadCountAsync` SQL query.

## New Files Created

### `BasicApi.Storage/Dto/CursorDto.cs`
A `readonly struct` that encodes a `(DateTime CreatedAt, Guid Id)` composite cursor as a URL-safe Base64 string. This ensures **deterministic, stable pagination** even when multiple messages share the same `created_at` timestamp.

Key design decisions:
- **Composite key** `(created_at, id)` — prevents data races where new messages with the same timestamp could cause missed or duplicated records
- **URL-safe Base64** encoding — no `+`, `/`, or `=` padding characters that cause issues in query strings
- **32 bytes total**: 8 bytes (`long` ticks) + 8 bytes (padding) + 16 bytes (Guid)

### `BasicApi.Storage/Dto/CursorResult.cs`
A generic wrapper that returns items plus an optional "extra" record. We fetch `limit + 1` rows in the query to detect `HasMore` without a second query.

### `BasicApi/Models/Dto/Message/CursorPaginatedResponse.cs`
The API-facing response DTO with `Items`, `NextCursor` (nullable), and `HasMore`.

## Modified Files

### `BasicApi.Storage/Interfaces/IMessageRepository.cs`
Added `GetMessagesCursorAsync(Guid chatId, string? cursor, int limit)` returning `CursorResult<Message>`.

### `BasicApi.Storage/Repositories/MessageRepository.cs`
Added the cursor-based implementation using **composite comparison** SQL:

```sql
WHERE (created_at < @beforeTime OR (created_at = @beforeTime AND id < @beforeId))
ORDER BY created_at DESC, id DESC
```

### `BasicApi/Services/IChatService.cs`
Added `GetChatMessagesCursorAsync` method to the interface.

### `BasicApi/Services/ChatService.cs`
- Implemented `GetChatMessagesCursorAsync` with authorization check, entity-to-DTO mapping, and next-cursor generation
- Also refactored `GetUserChatsAsync` to use the new cursor-based method (with limit=1) instead of the old `DateTime? before` approach for consistency

### `BasicApi/Features/Chats/ChatsHandler.cs`
Added `GetMessagesCursorAsync` handler with `try/catch` for `UnauthorizedAccessException`.

### `BasicApi/Features/Chats/ChatsController.cs`
- Added new **`GET /api/chats/{chatId}/messages/cursor`** endpoint
- Parameters: `cursor` (optional query string), `limit` (default 20, clamped 1–100)
- Returns `CursorPaginatedResponse<MessageDto>`
- Kept the legacy `GET /api/chats/{chatId}/messages` endpoint for backward compatibility

### `BasicApi.Storage/Repositories/ChatRepository.cs` (Bonus fix)
**Fixed the broken `GetUnreadCountAsync` SQL query.** The original had a syntax error:

```sql
-- BROKEN: stray FROM clause after WHERE
WHERE m.created_at > COALESCE((...), 'epoch') FROM chat_members cm WHERE ...
```

Fixed to use a proper correlated subquery:

```sql
WHERE m.is_deleted = false
  AND m.created_at > COALESCE(
      (SELECT created_at FROM messages WHERE id = (
          SELECT last_read_message_id FROM chat_members
          WHERE chat_id = @chatId AND user_id = @userId
      )),
      '1970-01-01'::timestamp
  )
```

## API Contract (New Endpoint)

```
GET /api/chats/{chatId}/messages/cursor?cursor={cursor}&limit=20
```

**Response:**
```json
{
  "items": [
    {
      "id": "guid",
      "senderId": "guid",
      "senderName": "string",
      "text": "string",
      "createdAt": "2024-01-01T00:00:00Z",
      "isRead": false
    }
  ],
  "nextCursor": "url-safe-base64-string",
  "hasMore": true
}
```

**Usage:**
1. First request: omit `cursor` → returns the most recent `limit` messages
2. Subsequent requests: pass `nextCursor` from previous response as `cursor` → returns older messages
3. When `nextCursor` is `null` and `hasMore` is `false`, there are no more pages
