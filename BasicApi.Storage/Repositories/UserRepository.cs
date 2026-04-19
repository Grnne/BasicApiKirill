using System.Data;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Dapper;

namespace BasicApi.Storage.Repositories;

public class UserRepository(IDbConnection connection) : IUserRepository
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

        return await connection.QueryFirstOrDefaultAsync<User>(sql, new { Value = usernameOrEmail });
    }

    public async Task<Guid> CreateAsync(User user, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO users (id, username, email, password_hash, display_name, created_at, last_login_at, is_active)
            VALUES (@Id, @Username, @Email, @PasswordHash, @DisplayName, @CreatedAt, @LastLoginAt, @IsActive)
            RETURNING id";

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

        return await connection.QueryFirstOrDefaultAsync<Guid>(sql, new { Value = usernameOrEmail });
    }
}