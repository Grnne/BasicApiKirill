using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class FolderRepository(IDbSession db) : IFolderRepository
{
    private const string Columns = @"
        f.id AS Id, f.user_id AS UserId, f.title AS Title, f.position AS Position,
        f.include_private AS IncludePrivate, f.include_groups AS IncludeGroups, f.only_unread AS OnlyUnread,
        f.created_at AS CreatedAt";

    private sealed class FolderChat
    {
        public Guid FolderId { get; set; }
        public Guid ChatId { get; set; }
        public int? PinnedPosition { get; set; }
    }

    public async Task<IReadOnlyList<Folder>> GetAllAsync(Guid userId, CancellationToken ct = default)
    {
        var folders = await db.QueryAsync<Folder>(
            $"SELECT {Columns} FROM folders f WHERE f.user_id = @userId ORDER BY f.position, f.created_at", new { userId }, ct);
        await FillChatsAsync(folders, ct);
        return folders;
    }

    public async Task<Folder?> GetAsync(Guid userId, Guid folderId, CancellationToken ct = default)
    {
        var folder = await db.QueryFirstOrDefaultAsync<Folder>(
            $"SELECT {Columns} FROM folders f WHERE f.id = @folderId AND f.user_id = @userId", new { userId, folderId }, ct);
        if (folder is not null)
            await FillChatsAsync([folder], ct);
        return folder;
    }

    private async Task FillChatsAsync(IReadOnlyList<Folder> folders, CancellationToken ct)
    {
        if (folders.Count == 0)
            return;
        var rows = await db.QueryAsync<FolderChat>(@"
            SELECT folder_id AS FolderId, chat_id AS ChatId, pinned_position AS PinnedPosition
            FROM folder_chats WHERE folder_id = ANY(@ids)
            ORDER BY pinned_position NULLS LAST, chat_id",
            new { ids = folders.Select(f => f.Id).ToArray() }, ct);
        foreach (var folder in folders)
        {
            var own = rows.Where(r => r.FolderId == folder.Id).ToList();
            folder.ChatIds = [.. own.Select(r => r.ChatId)];
            folder.PinnedChatIds = [.. own.Where(r => r.PinnedPosition is not null).Select(r => r.ChatId)];
        }
    }

    public Task SaveAsync(Folder folder, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await db.ExecuteAsync(@"
                INSERT INTO folders (id, user_id, title, position, include_private, include_groups, only_unread, created_at)
                VALUES (@Id, @UserId, @Title, @Position, @IncludePrivate, @IncludeGroups, @OnlyUnread, @CreatedAt)
                ON CONFLICT (id) DO UPDATE
                SET title = EXCLUDED.title, include_private = EXCLUDED.include_private,
                    include_groups = EXCLUDED.include_groups, only_unread = EXCLUDED.only_unread",
                folder, ct);
            await db.ExecuteAsync("DELETE FROM folder_chats WHERE folder_id = @Id", folder, ct);
            await db.ExecuteAsync(@"
                INSERT INTO folder_chats (folder_id, chat_id, pinned_position)
                SELECT @folderId, c.id, p.position
                FROM unnest(@chatIds) AS c(id)
                LEFT JOIN unnest(@pinned) WITH ORDINALITY AS p(id, position) ON p.id = c.id",
                new
                {
                    folderId = folder.Id,
                    chatIds = folder.ChatIds.Union(folder.PinnedChatIds).ToArray(),
                    pinned = folder.PinnedChatIds.ToArray()
                }, ct);
            return true;
        }, ct: ct);

    public async Task<bool> DeleteAsync(Guid userId, Guid folderId, CancellationToken ct = default) =>
        await db.ExecuteAsync("DELETE FROM folders WHERE id = @folderId AND user_id = @userId", new { userId, folderId }, ct) > 0;

    public Task SetOrderAsync(Guid userId, IReadOnlyList<Guid> folderIds, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            UPDATE folders f SET position = o.position
            FROM unnest(@folderIds) WITH ORDINALITY AS o(id, position)
            WHERE f.id = o.id AND f.user_id = @userId",
            new { userId, folderIds = folderIds.ToArray() }, ct);

    public Task<IReadOnlyList<Guid>> GetOwnChatsAsync(Guid userId, IReadOnlyCollection<Guid> chatIds, CancellationToken ct = default) =>
        db.QueryAsync<Guid>(
            "SELECT chat_id FROM chat_members WHERE user_id = @userId AND chat_id = ANY(@chatIds)",
            new { userId, chatIds = chatIds.Distinct().ToArray() }, ct);
}
