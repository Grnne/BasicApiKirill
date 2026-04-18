using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct = default);
    Task<Guid> CreateAsync(User user, CancellationToken ct = default);
}