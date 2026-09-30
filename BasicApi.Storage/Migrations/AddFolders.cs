using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Folders, up to 20 per user: the chats listed in one (possibly pinned inside it) and, by filters,
/// all private chats or all groups, optionally only unread ones.
/// </summary>
[Migration(28)]
public class AddFolders : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE folders (
                id uuid PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                title text NOT NULL,
                position integer NOT NULL,
                include_private boolean NOT NULL DEFAULT false,
                include_groups boolean NOT NULL DEFAULT false,
                only_unread boolean NOT NULL DEFAULT false,
                created_at timestamptz NOT NULL
            )");
        Execute.Sql("CREATE INDEX ix_folders_user ON folders (user_id, position)");
        Execute.Sql(@"
            CREATE TABLE folder_chats (
                folder_id uuid NOT NULL REFERENCES folders (id) ON DELETE CASCADE,
                chat_id uuid NOT NULL REFERENCES chats (id) ON DELETE CASCADE,
                pinned_position integer NULL,
                PRIMARY KEY (folder_id, chat_id)
            )");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS folder_chats");
        Execute.Sql("DROP TABLE IF EXISTS folders");
    }
}
