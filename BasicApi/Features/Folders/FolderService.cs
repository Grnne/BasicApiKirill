using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Features.Folders;

/// <summary>The user's folders of chats; the other devices follow by <c>FoldersChanged</c>.</summary>
public interface IFolderService
{
    Task<IReadOnlyList<FolderDto>> GetAllAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Errors: 400 <c>INVALID_TITLE</c>/<c>TOO_MANY_FOLDERS</c>/<c>INVALID_REQUEST</c>.</summary>
    Task<FolderDto> CreateAsync(Guid userId, SaveFolderDto request, CancellationToken ct = default);

    /// <summary>Errors: 400 <c>INVALID_TITLE</c>/<c>INVALID_REQUEST</c>, 404 <c>FOLDER_NOT_FOUND</c>.</summary>
    Task<FolderDto> UpdateAsync(Guid userId, Guid folderId, SaveFolderDto changes, CancellationToken ct = default);

    /// <summary>Errors: 404 <c>FOLDER_NOT_FOUND</c>.</summary>
    Task DeleteAsync(Guid userId, Guid folderId, CancellationToken ct = default);

    /// <summary>Exactly the user's folders in a new order. Errors: 400 <c>INVALID_REQUEST</c>.</summary>
    Task<IReadOnlyList<FolderDto>> ReorderAsync(Guid userId, IReadOnlyList<Guid> folderIds, CancellationToken ct = default);

    /// <summary>
    /// The folder's chats: the first page starts with the ones pinned in it (outside the limit),
    /// then the others by activity. Errors: 400 <c>INVALID_CURSOR</c>, 404 <c>FOLDER_NOT_FOUND</c>.
    /// </summary>
    Task<CursorPaginatedResponse<ChatListItemDto>> GetChatsAsync(
        Guid userId, Guid folderId, string? cursor, int limit, CancellationToken ct = default);
}

public sealed class FolderService(
    IDbSession db,
    IFolderRepository folders,
    IChatRepository chats,
    IChatStateRepository states,
    IChatEventPublisher events,
    TimeProvider time) : IFolderService
{
    public const int MaxFolders = 20;
    public const int MaxTitleLength = 64;
    public const int MaxChats = 200;
    public const int MaxPinned = 10;

    public async Task<IReadOnlyList<FolderDto>> GetAllAsync(Guid userId, CancellationToken ct = default) =>
        [.. (await folders.GetAllAsync(userId, ct)).Select(ToDto)];

    public async Task<FolderDto> CreateAsync(Guid userId, SaveFolderDto request, CancellationToken ct = default)
    {
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = Title(request.Title),
            IncludePrivate = request.IncludePrivate ?? false,
            IncludeGroups = request.IncludeGroups ?? false,
            OnlyUnread = request.OnlyUnread ?? false,
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        await SetChatsAsync(userId, folder, request.ChatIds ?? [], request.PinnedChatIds ?? [], ct);

        return await ChangeAsync(userId, async (existing, ct) =>
        {
            if (existing.Count >= MaxFolders)
                throw new BadRequestException($"At most {MaxFolders} folders are allowed", "TOO_MANY_FOLDERS");
            folder.Position = existing.Count + 1;
            await folders.SaveAsync(folder, ct);
            return folder;
        }, ct);
    }

    public async Task<FolderDto> UpdateAsync(Guid userId, Guid folderId, SaveFolderDto changes, CancellationToken ct = default)
    {
        var folder = await folders.GetAsync(userId, folderId, ct) ?? throw NotFound();
        if (changes.Title is not null)
            folder.Title = Title(changes.Title);
        folder.IncludePrivate = changes.IncludePrivate ?? folder.IncludePrivate;
        folder.IncludeGroups = changes.IncludeGroups ?? folder.IncludeGroups;
        folder.OnlyUnread = changes.OnlyUnread ?? folder.OnlyUnread;
        await SetChatsAsync(userId, folder, changes.ChatIds ?? folder.ChatIds, changes.PinnedChatIds ?? folder.PinnedChatIds, ct);

        return await ChangeAsync(userId, async (_, ct) =>
        {
            await folders.SaveAsync(folder, ct);
            return folder;
        }, ct);
    }

    public async Task DeleteAsync(Guid userId, Guid folderId, CancellationToken ct = default) =>
        await ChangeAsync(userId, async (_, ct) =>
        {
            if (!await folders.DeleteAsync(userId, folderId, ct))
                throw NotFound();
            return (Folder?)null;
        }, ct);

    public async Task<IReadOnlyList<FolderDto>> ReorderAsync(Guid userId, IReadOnlyList<Guid> folderIds, CancellationToken ct = default)
    {
        await ChangeAsync(userId, async (existing, ct) =>
        {
            if (folderIds.Count != existing.Count || folderIds.Distinct().Count() != folderIds.Count ||
                !existing.All(f => folderIds.Contains(f.Id)))
                throw new BadRequestException("folderIds must be exactly the folders, in the new order", "INVALID_REQUEST");
            await folders.SetOrderAsync(userId, folderIds, ct);
            return (Folder?)null;
        }, ct);
        return await GetAllAsync(userId, ct);
    }

    public async Task<CursorPaginatedResponse<ChatListItemDto>> GetChatsAsync(
        Guid userId, Guid folderId, string? cursor, int limit, CancellationToken ct = default)
    {
        ChatListCursor? before = null;
        if (!string.IsNullOrEmpty(cursor))
            before = ChatListCursor.TryDecode(cursor, out var parsed)
                ? parsed
                : throw new BadRequestException("Cursor is malformed", "INVALID_CURSOR");
        var folder = await folders.GetAsync(userId, folderId, ct) ?? throw NotFound();

        var pinned = before is null ? await chats.GetFolderChatsAsync(userId, folder, pinned: true, null, MaxPinned, ct) : [];
        var rows = await chats.GetFolderChatsAsync(userId, folder, pinned: false, before, limit + 1, ct);
        var page = rows.Take(limit).ToList();
        var hasMore = rows.Count > limit;
        return new CursorPaginatedResponse<ChatListItemDto>
        {
            Items = [.. pinned.Concat(page).Select(ChatListItemMapper.Map)],
            HasMore = hasMore,
            NextCursor = hasMore ? new ChatListCursor(page[^1].LastActivityAt, page[^1].ChatId).Encode() : null
        };
    }

    /// <summary>
    /// One change of the user's folders at a time (the limit holds), then all of them to the
    /// user's devices.
    /// </summary>
    private async Task<FolderDto> ChangeAsync(
        Guid userId, Func<IReadOnlyList<Folder>, CancellationToken, Task<Folder?>> change, CancellationToken ct) =>
        await db.InTransactionAsync(async ct =>
        {
            await states.LockUserAsync(userId, ct);
            var changed = await change(await folders.GetAllAsync(userId, ct), ct);
            var all = (await folders.GetAllAsync(userId, ct)).Select(ToDto).ToList();
            await events.FoldersChangedAsync(new FoldersDto { Folders = all }, userId, ct);
            return changed is null ? new FolderDto() : all.First(f => f.Id == changed.Id);
        }, ct: ct);

    /// <summary>Only the user's own chats, within the limits; the pinned ones are in the folder too.</summary>
    private async Task SetChatsAsync(
        Guid userId, Folder folder, IReadOnlyList<Guid> chatIds, IReadOnlyList<Guid> pinnedIds, CancellationToken ct)
    {
        var all = chatIds.Union(pinnedIds).ToList();
        if (all.Count > MaxChats || pinnedIds.Count > MaxPinned || pinnedIds.Distinct().Count() != pinnedIds.Count)
            throw new BadRequestException(
                $"A folder lists at most {MaxChats} chats and pins at most {MaxPinned}", "INVALID_REQUEST");
        if (all.Count > 0 && (await folders.GetOwnChatsAsync(userId, all, ct)).Count != all.Count)
            throw new BadRequestException("A folder may list only the user's chats", "INVALID_REQUEST");
        folder.ChatIds = all;
        folder.PinnedChatIds = [.. pinnedIds];
    }

    private static string Title(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 and <= MaxTitleLength
            ? trimmed
            : throw new BadRequestException($"Folder title must be 1 to {MaxTitleLength} characters", "INVALID_TITLE");
    }

    private static NotFoundException NotFound() => new("Folder not found", "FOLDER_NOT_FOUND");

    public static FolderDto ToDto(Folder f) => new()
    {
        Id = f.Id,
        Title = f.Title,
        IncludePrivate = f.IncludePrivate,
        IncludeGroups = f.IncludeGroups,
        OnlyUnread = f.OnlyUnread,
        ChatIds = f.ChatIds,
        PinnedChatIds = f.PinnedChatIds
    };
}
