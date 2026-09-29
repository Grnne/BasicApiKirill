using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// The chat list in pages (plan 2, F3.6): chats.last_activity_at — when the chat last got a
/// message, or was created — is kept by every send, so the list is ordered without looking
/// into each chat's messages. The same for all members: deleting the last message does not move
/// the chat down, as in Telegram.
/// </summary>
[Migration(21)]
public class AddChatLastActivity : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE chats ADD COLUMN last_activity_at timestamptz NULL");
        Execute.Sql(@"
            UPDATE chats c SET last_activity_at = GREATEST(c.created_at, COALESCE(
                (SELECT MAX(m.created_at) FROM messages m WHERE m.chat_id = c.id AND m.deleted_at IS NULL),
                c.created_at))");
        Execute.Sql("ALTER TABLE chats ALTER COLUMN last_activity_at SET DEFAULT now(), ALTER COLUMN last_activity_at SET NOT NULL");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE chats DROP COLUMN last_activity_at");
    }
}
