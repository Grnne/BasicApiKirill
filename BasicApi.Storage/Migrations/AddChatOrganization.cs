using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// How a member keeps a chat (plan 2, F6.1, D9): pinned to the top of the list (the position, 1 —
/// the top), archived, muted until a moment (far future — for good). All per member.
/// </summary>
[Migration(27)]
public class AddChatOrganization : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE chat_members
                ADD COLUMN pinned_position integer NULL,
                ADD COLUMN archived_at timestamptz NULL,
                ADD COLUMN muted_until timestamptz NULL");
        // A user's pinned chats: at most ten, read on every first page of the list.
        Execute.Sql("CREATE INDEX ix_chat_members_pinned ON chat_members (user_id, pinned_position) WHERE pinned_position IS NOT NULL");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_chat_members_pinned");
        Execute.Sql("ALTER TABLE chat_members DROP COLUMN IF EXISTS pinned_position, DROP COLUMN IF EXISTS archived_at, DROP COLUMN IF EXISTS muted_until");
    }
}
