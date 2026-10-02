using BasicApi.Storage.Entities;
using BasicApi.Storage.Exceptions;
using BasicApi.Storage.Interfaces;
using Npgsql;

namespace BasicApi.Storage.Repositories;

public class UserRepository(IDbSession db) : IUserRepository
{
    public async Task<User?> GetByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                id as Id, 
                username as Username, 
                email as Email, 
                password_hash as PasswordHash, 
                display_name as DisplayName, avatar_attachment_id as AvatarAttachmentId,
                created_at as CreatedAt, 
                last_login_at as LastLoginAt, 
                is_active as IsActive
            FROM users 
            -- Case and surrounding spaces do not matter: Alice, alice and ' alice ' are one login.
            WHERE username_normalized = lower(trim(@Value)) OR email_normalized = lower(trim(@Value))
            LIMIT 1";

        return await db.QueryFirstOrDefaultAsync<User>(sql, new { Value = usernameOrEmail }, ct);
    }

    public async Task<Guid> CreateAsync(User user, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO users (id, username, email, password_hash, display_name, created_at, last_login_at, is_active)
            VALUES (@Id, @Username, @Email, @PasswordHash, @DisplayName, @CreatedAt, @LastLoginAt, @IsActive)
            RETURNING id";

        try
        {
            return await db.ExecuteScalarAsync<Guid>(sql, user, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Two parallel registrations for the same username/email: the handler's checks
            // are not atomic, the unique index catches it. Translate it into a domain error
            // so that the client gets 409, not 500.
            throw new DuplicateKeyException("User with this username or email already exists", ex);
        }
    }

    public async Task UpdateLastLoginAsync(Guid userId, DateTime lastLoginAt, CancellationToken ct = default)
    {
        const string sql = "UPDATE users SET last_login_at = @lastLoginAt WHERE id = @userId";

        await db.ExecuteAsync(sql, new { userId, lastLoginAt }, ct);
    }

    public async Task<Guid?> GetIdByUsernameAsync(string username, CancellationToken ct = default)
    {
        // Login only and active users only: lookup by email is not allowed - it is private data.
        const string sql = @"
            SELECT id
            FROM users
            WHERE username_normalized = lower(trim(@username)) AND is_active = true";

        return await db.QueryFirstOrDefaultAsync<Guid?>(sql, new { username }, ct);
    }

    public Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        db.QueryAsync<User>(@"
            SELECT id AS Id, username AS Username, email AS Email, display_name AS DisplayName, avatar_attachment_id AS AvatarAttachmentId,
                   last_seen_at AS LastSeenAt,
                   created_at AS CreatedAt, last_login_at AS LastLoginAt, is_active AS IsActive
            FROM users
            WHERE id = ANY(@ids)",
            new { ids = ids.Distinct().ToArray() }, ct);

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                id as Id,
                username as Username,
                email as Email,
                password_hash as PasswordHash,
                display_name as DisplayName,
                avatar_attachment_id as AvatarAttachmentId,
                created_at as CreatedAt,
                last_login_at as LastLoginAt,
                is_active as IsActive
            FROM users
            WHERE id = @Id
            LIMIT 1";

        return await db.QueryFirstOrDefaultAsync<User>(sql, new { Id = id }, ct);
    }

    public Task SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken ct = default) =>
        db.ExecuteAsync("UPDATE users SET password_hash = @passwordHash WHERE id = @userId", new { userId, passwordHash }, ct);

    public async Task<bool> SetDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default) =>
        await db.ExecuteAsync(
            "UPDATE users SET display_name = @displayName WHERE id = @userId AND display_name <> @displayName",
            new { userId, displayName }, ct) > 0;

    public Task SetLastSeenAsync(Guid userId, DateTime at, CancellationToken ct = default) =>
        db.ExecuteAsync("UPDATE users SET last_seen_at = @at WHERE id = @userId", new { userId, at }, ct);

    public async Task<bool> SetAvatarAsync(Guid userId, Guid? attachmentId, CancellationToken ct = default) =>
        await db.ExecuteAsync(@"
            UPDATE users SET avatar_attachment_id = @attachmentId
            WHERE id = @userId AND avatar_attachment_id IS DISTINCT FROM @attachmentId",
            new { userId, attachmentId }, ct) > 0;

    public async Task<IEnumerable<User>> SearchByDisplayNameOrUsernameAsync(
        string query, Guid excludeUserId, int limit, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                id as Id,
                username as Username,
                email as Email,
                password_hash as PasswordHash,
                display_name as DisplayName,
                avatar_attachment_id as AvatarAttachmentId,
                created_at as CreatedAt,
                last_login_at as LastLoginAt,
                is_active as IsActive
            FROM users
            WHERE is_active = true
              AND id != @ExcludeUserId
              AND (display_name ILIKE @Pattern OR username ILIKE @Pattern)
            ORDER BY display_name, username
            LIMIT @Limit";

        return await db.QueryAsync<User>(sql, new
        {
            ExcludeUserId = excludeUserId,
            Pattern = Like.Contains(query),
            Limit = limit
        }, ct);
    }

    public async Task<int> CountBySearchQueryAsync(string query, Guid excludeUserId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM users
            WHERE is_active = true
              AND id != @ExcludeUserId
              AND (display_name ILIKE @Pattern OR username ILIKE @Pattern)";

        return await db.ExecuteScalarAsync<int>(sql, new
        {
            ExcludeUserId = excludeUserId,
            Pattern = Like.Contains(query)
        }, ct);
    }
}
