using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class InviteRepository(IDbSession db) : IInviteRepository
{
    public Task CreateAsync(string codeHash, Guid createdBy, DateTime createdAt, DateTime expiresAt, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            INSERT INTO invites (code_hash, created_by, created_at, expires_at)
            VALUES (@codeHash, @createdBy, @createdAt, @expiresAt)",
            new { codeHash, createdBy, createdAt, expiresAt }, ct);

    public Task<int> CountOpenAsync(Guid createdBy, DateTime now, CancellationToken ct = default) =>
        db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM invites WHERE created_by = @createdBy AND used_by IS NULL AND expires_at > @now",
            new { createdBy, now }, ct);

    public Task<bool> IsOpenAsync(string codeHash, DateTime now, CancellationToken ct = default) =>
        db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM invites WHERE code_hash = @codeHash AND used_by IS NULL AND expires_at > @now)",
            new { codeHash, now }, ct);

    public async Task<bool> UseAsync(string codeHash, Guid userId, DateTime now, CancellationToken ct = default) =>
        // One statement: of two registrations with the same code, the second finds it used.
        await db.ExecuteAsync(@"
            UPDATE invites SET used_by = @userId, used_at = @now
            WHERE code_hash = @codeHash AND used_by IS NULL AND expires_at > @now",
            new { codeHash, userId, now }, ct) == 1;
}
