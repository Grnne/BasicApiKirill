using System.Data;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Dapper;

namespace BasicApi.Storage.Repositories;

public class UserRepository(IDbConnectionFactory connectionFactory) : IUserRepository
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
            WHERE username = @Value OR email = @Value
            LIMIT 1";

        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(sql, new { Value = usernameOrEmail });
    }

    public async Task<Guid> CreateAsync(User user, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO users (id, username, email, password_hash, display_name, created_at, last_login_at, is_active)
            VALUES (@Id, @Username, @Email, @PasswordHash, @DisplayName, @CreatedAt, @LastLoginAt, @IsActive)
            RETURNING id";

        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(sql, user);
    }

    public async Task<Guid?> GetIdByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                id as Id
            FROM users 
            WHERE username = @Value OR email = @Value
            LIMIT 1";

        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Guid>(sql, new { Value = usernameOrEmail });
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

        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(sql, new { Id = id });
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

        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryAsync<User>(sql, new
        {
            ExcludeUserId = excludeUserId,
            Pattern = $"%{query}%",
            Limit = limit
        });
    }

    public async Task<int> CountBySearchQueryAsync(string query, Guid excludeUserId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM users
            WHERE is_active = true
              AND id != @ExcludeUserId
              AND (display_name ILIKE @Pattern OR username ILIKE @Pattern)";

        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(sql, new
        {
            ExcludeUserId = excludeUserId,
            Pattern = $"%{query}%"
        });
    }
}
