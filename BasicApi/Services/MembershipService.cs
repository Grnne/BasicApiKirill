using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

/// <summary>
/// Who is in which chat, and with which role.
/// What a member is allowed to do is decided by <see cref="IChatPolicy"/>, not by this service.
/// </summary>
public interface IMembershipService
{
    Task<bool> IsMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default);

    /// <summary>The member with their role and the chat's type and settings; null — not a member.</summary>
    Task<ChatMember?> GetMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default);

    /// <summary>Chat participants: the recipients of its events.</summary>
    Task<IReadOnlyList<Guid>> GetMemberIdsAsync(Guid chatId, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetChatIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Everyone the user shares a chat with (excluding the user).</summary>
    Task<IReadOnlyList<Guid>> GetContactIdsAsync(Guid userId, CancellationToken ct = default);
}

public sealed class MembershipService(IChatRepository chatRepository) : IMembershipService
{
    public Task<bool> IsMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default) =>
        chatRepository.IsMemberAsync(chatId, userId, ct);

    public Task<ChatMember?> GetMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default) =>
        chatRepository.GetMemberAsync(chatId, userId, ct);

    public Task<IReadOnlyList<Guid>> GetMemberIdsAsync(Guid chatId, CancellationToken ct = default) =>
        chatRepository.GetMemberIdsAsync(chatId, ct);

    public Task<IReadOnlyList<Guid>> GetChatIdsAsync(Guid userId, CancellationToken ct = default) =>
        chatRepository.GetUserChatIdsAsync(userId, ct);

    public async Task<IReadOnlyList<Guid>> GetContactIdsAsync(Guid userId, CancellationToken ct = default) =>
        await chatRepository.GetAllChatMembersAsync(userId, ct);
}
