using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services.Events;
using BasicApi.Storage;
using ChatListCursor = BasicApi.Storage.Dto.ChatListCursor;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

public sealed class ChatService(
    IDbSession db,
    IChatRepository chatRepository,
    IUserRepository userRepository,
    IChatPolicy policy,
    IPresenceService presence,
    IChatEventPublisher events,
    IPrivacyRepository? privacy = null) : IChatService
{
    public async Task<List<ChatListItemDto>> GetUserChatsAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await chatRepository.GetUserChatsBatchedAsync(userId, ct);
        return [.. rows.Select(ChatListItemMapper.Map)];
    }

    public async Task<CursorPaginatedResponse<ChatListItemDto>> GetUserChatsPageAsync(
        Guid userId, string? cursor, int limit, CancellationToken ct = default)
    {
        ChatListCursor? before = null;
        if (!string.IsNullOrEmpty(cursor))
            before = ChatListCursor.TryDecode(cursor, out var parsed)
                ? parsed
                : throw new BadRequestException("Cursor is malformed", "INVALID_CURSOR");

        var rows = await chatRepository.GetUserChatsPageAsync(userId, before, limit + 1, ct);
        var page = rows.Take(limit).ToList();
        var hasMore = rows.Count > limit;
        return new CursorPaginatedResponse<ChatListItemDto>
        {
            Items = [.. page.Select(ChatListItemMapper.Map)],
            HasMore = hasMore,
            NextCursor = hasMore ? new ChatListCursor(page[^1].LastActivityAt, page[^1].ChatId).Encode() : null
        };
    }

    public async Task<ChatListItemDto> GetChatListItemAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        // Same 404/403 semantics as GetChatDetailsAsync — the projection query
        // itself cannot tell "chat missing" from "not a member".
        _ = await chatRepository.GetByIdAsync(chatId, ct)
            ?? throw ChatNotFound();

        await policy.DemandReadAsync(userId, chatId, ct);

        var row = await chatRepository.GetChatListItemAsync(chatId, userId, ct)
            ?? throw ChatNotFound();

        return ChatListItemMapper.Map(row);
    }

    public async Task<ChatDetailDto> GetChatDetailsAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        var chat = await chatRepository.GetByIdAsync(chatId, ct)
            ?? throw ChatNotFound();

        await policy.DemandReadAsync(userId, chatId, ct);

        var participants = await chatRepository.GetChatParticipantsAsync(chatId, ct);
        var me = await chatRepository.GetMemberAsync(chatId, userId, ct);
        // Those who blocked the caller do not show their avatar to them.
        var hidden = privacy is null
            ? new HashSet<Guid>()
            : await privacy.GetBlockersAsync(userId, [.. participants.Select(p => p.UserId)], ct);
        var isGroup = chat.Type == ChatTypes.Group;

        return new ChatDetailDto
        {
            ChatId = chat.Id,
            Type = chat.Type,
            Title = chat.Title,
            Participants = [.. participants.Select(p => new ChatParticipantDto
            {
                UserId = p.UserId,
                DisplayName = p.DisplayName,
                Username = p.Username,
                Role = p.Role,
                AvatarId = hidden.Contains(p.UserId) ? null : p.AvatarId
            })],
            AvatarId = chat.Type == ChatTypes.Private
                ? participants.FirstOrDefault(p => p.UserId != userId && !hidden.Contains(p.UserId))?.AvatarId
                : chat.AvatarAttachmentId,
            CreatedBy = chat.CreatedBy,
            MyRole = me?.Role ?? ChatRoles.Member,
            MyPermissions = isGroup && me is not null ? GroupRights.Effective(me) : null,
            MemberPermissions = isGroup ? GroupRights.MemberPermissions(chat.SettingsJson) : null
        };
    }

    public async Task<PrivateChatResult> GetOrCreatePrivateChatAsync(
        Guid userId, Guid otherUserId, CancellationToken ct = default)
    {
        if (userId == otherUserId)
            throw new BadRequestException("Cannot create chat with yourself", "SELF_CHAT");

        // Without this check a nonexistent counterpart broke the insert on the foreign key (500).
        // A deactivated one is also 404: there is nobody to write to.
        var other = await userRepository.GetByIdAsync(otherUserId, ct);
        if (other is null || !other.IsActive)
            throw new NotFoundException("User not found", "USER_NOT_FOUND");

        // Privacy decides only whether a new chat may start: an existing one keeps working (D11).
        if (await chatRepository.GetPrivateChatIdAsync(userId, otherUserId, ct) is null)
            (await policy.CanStartPrivateChatAsync(userId, otherUserId, ct)).Demand();

        // The chat and its events are one transaction.
        var result = await db.InTransactionAsync(async ct =>
        {
            var (chatId, created) = await chatRepository.GetOrCreatePrivateChatAsync(userId, otherUserId, ct);

            var own = ChatListItemMapper.Map(await chatRepository.GetChatListItemAsync(chatId, userId, ct)
                ?? throw ChatNotFound());

            if (created)
            {
                // The second participant gets their own card: the counterpart in it is the chat creator.
                var theirs = ChatListItemMapper.Map(await chatRepository.GetChatListItemAsync(chatId, otherUserId, ct)
                    ?? throw ChatNotFound());
                await events.ChatCreatedAsync(otherUserId, theirs, ct: ct);
                await events.ChatCreatedAsync(userId, own, live: false, ct);
            }

            return new PrivateChatResult(own, created);
        }, ct: ct);

        if (result.Created)
            await presence.IntroduceAsync(userId, otherUserId, CancellationToken.None);

        return result;
    }

    public Task<PrivateChatResult> GetOrCreateSavedChatAsync(Guid userId, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            var (chatId, created) = await chatRepository.GetOrCreateSavedChatAsync(userId, ct);
            var item = ChatListItemMapper.Map(await chatRepository.GetChatListItemAsync(chatId, userId, ct)
                ?? throw ChatNotFound());

            if (created)
                await events.ChatCreatedAsync(userId, item, live: false, ct);

            return new PrivateChatResult(item, created);
        }, ct: ct);

    public async Task<SearchChatsResponseDto> SearchChatsAsync(
        Guid userId, string query, string? type, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new BadRequestException("Query cannot be empty", "INVALID_QUERY");

        var typeFilter = type?.ToLowerInvariant();

        // Outside a transaction each query takes its own connection — can run in parallel.
        var rowsTask = chatRepository.SearchChatsBatchedAsync(userId, query, typeFilter, limit, ct);
        var countTask = chatRepository.CountChatsByQueryAsync(userId, query, typeFilter, ct);

        await Task.WhenAll(rowsTask, countTask);

        return new SearchChatsResponseDto
        {
            Items = [.. rowsTask.Result.Select(ChatListItemMapper.Map)],
            Query = query,
            TotalCount = countTask.Result
        };
    }

    private static NotFoundException ChatNotFound() => new("Chat not found", "CHAT_NOT_FOUND");
}
