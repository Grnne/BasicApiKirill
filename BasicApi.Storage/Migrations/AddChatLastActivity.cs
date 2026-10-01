using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// chats.last_activity_at, kept by every send, orders the chat list without looking into messages.
/// It is the same for all members: deleting the last message does not move the chat down.
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
