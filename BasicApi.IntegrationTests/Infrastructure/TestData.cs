using BasicApi.Storage.Interfaces;
using Dapper;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>
/// Наполнение базы прямыми INSERT'ами: время и идентификаторы задаются явно,
/// чтобы тесты могли строить граничные случаи (одинаковый created_at и т.п.).
/// </summary>
public sealed class TestData(IDbConnectionFactory connectionFactory)
{
    /// <summary>Опорная точка времени; округлена до микросекунд, как хранит Postgres.</summary>
    public static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public async Task<Guid> UserAsync(string username, string? displayName = null, bool isActive = true)
    {
        var id = Guid.NewGuid();
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            INSERT INTO users (id, username, email, password_hash, display_name, created_at, is_active)
            VALUES (@id, @username, @email, 'x', @displayName, @createdAt, @isActive)",
            new
            {
                id,
                username,
                email = $"{username}@test.local",
                displayName = displayName ?? username,
                createdAt = T0,
                isActive
            });
        return id;
    }

    public async Task<Guid> PrivateChatAsync(Guid a, Guid b, DateTime? createdAt = null)
    {
        var id = await ChatAsync("private", null, [a, b], createdAt);
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE chats SET private_key = LEAST(@a, @b)::text || ':' || GREATEST(@a, @b)::text WHERE id = @id",
            new { id, a, b });
        return id;
    }

    public Task<Guid> GroupChatAsync(string title, Guid[] members, DateTime? createdAt = null) =>
        ChatAsync("group", title, members, createdAt);

    private async Task<Guid> ChatAsync(string type, string? title, Guid[] members, DateTime? createdAt)
    {
        var id = Guid.NewGuid();
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "INSERT INTO chats (id, title, type, created_at) VALUES (@id, @title, @type, @createdAt)",
            new { id, title, type, createdAt = createdAt ?? T0 });

        foreach (var userId in members)
        {
            await connection.ExecuteAsync(
                "INSERT INTO chat_members (chat_id, user_id, joined_at) VALUES (@id, @userId, @joinedAt)",
                new { id, userId, joinedAt = createdAt ?? T0 });
        }

        return id;
    }

    public async Task<Guid> MessageAsync(
        Guid chatId, Guid senderId, string text, DateTime createdAt, Guid? id = null, bool isDeleted = false)
    {
        var messageId = id ?? Guid.NewGuid();
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            INSERT INTO messages (id, chat_id, sender_id, text, created_at, is_deleted)
            VALUES (@messageId, @chatId, @senderId, @text, @createdAt, @isDeleted)",
            new { messageId, chatId, senderId, text, createdAt, isDeleted });
        return messageId;
    }

    public async Task MarkReadAsync(Guid chatId, Guid userId, Guid messageId)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE chat_members SET last_read_message_id = @messageId WHERE chat_id = @chatId AND user_id = @userId",
            new { chatId, userId, messageId });
    }
}
