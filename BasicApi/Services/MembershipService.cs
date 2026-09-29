using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

/// <summary>
/// Кто в каком чате. Сейчас состав меняется только созданием личного чата;
/// вступление, выход и роли в группах (план 2) появятся здесь же.
/// Что участнику можно — решает <see cref="IChatPolicy"/>, не этот сервис.
/// </summary>
public interface IMembershipService
{
    Task<bool> IsMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default);

    /// <summary>Участники чата — получатели его событий.</summary>
    Task<IReadOnlyList<Guid>> GetMemberIdsAsync(Guid chatId, CancellationToken ct = default);

    /// <summary>Чаты пользователя.</summary>
    Task<IReadOnlyList<Guid>> GetChatIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Все, с кем у пользователя есть общий чат (без него самого).</summary>
    Task<IReadOnlyList<Guid>> GetContactIdsAsync(Guid userId, CancellationToken ct = default);
}

public sealed class MembershipService(IChatRepository chatRepository) : IMembershipService
{
    public Task<bool> IsMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default) =>
        chatRepository.IsMemberAsync(chatId, userId, ct);

    public Task<IReadOnlyList<Guid>> GetMemberIdsAsync(Guid chatId, CancellationToken ct = default) =>
        chatRepository.GetMemberIdsAsync(chatId, ct);

    public Task<IReadOnlyList<Guid>> GetChatIdsAsync(Guid userId, CancellationToken ct = default) =>
        chatRepository.GetUserChatIdsAsync(userId, ct);

    public async Task<IReadOnlyList<Guid>> GetContactIdsAsync(Guid userId, CancellationToken ct = default) =>
        await chatRepository.GetAllChatMembersAsync(userId, ct);
}
