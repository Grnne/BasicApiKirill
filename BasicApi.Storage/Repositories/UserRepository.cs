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
                display_name as DisplayName, 
                created_at as CreatedAt, 
                last_login_at as LastLoginAt, 
                is_active as IsActive
            FROM users 
            -- Регистр и пробелы по краям не важны: Alice, alice и ' alice ' — один логин.
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
            // Две параллельные регистрации на один username/email: проверки в хендлере
            // не атомарны, ловит уникальный индекс. Переводим в доменную ошибку,
            // чтобы клиент получил 409, а не 500.
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
        // Только логин и только активные: по почте искать нельзя — это приватные данные.
        const string sql = @"
            SELECT id
            FROM users
            WHERE username_normalized = lower(trim(@username)) AND is_active = true";

        return await db.QueryFirstOrDefaultAsync<Guid?>(sql, new { username }, ct);
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                id as Id,
                username as Username,
                email as Email,
                password_hash as PasswordHash,
                display_name as DisplayName,
                created_at as CreatedAt,
                last_login_at as LastLoginAt,
                is_active as IsActive
            FROM users
            WHERE id = @Id
            LIMIT 1";

        return await db.QueryFirstOrDefaultAsync<User>(sql, new { Id = id }, ct);
    }

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
            Pattern = $"%{query}%",
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
            Pattern = $"%{query}%"
        }, ct);
    }
}
