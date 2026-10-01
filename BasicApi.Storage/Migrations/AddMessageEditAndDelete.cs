using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Editing and deleting: a message deleted for everyone stays as a tombstone with its text wiped (its seq,
/// replies and read pointers stay valid); hidden_messages is "delete for me".
/// </summary>
[Migration(13)]
public class AddMessageEditAndDelete : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE messages
                ADD COLUMN type text NOT NULL DEFAULT 'text',
                ADD COLUMN edited_at timestamptz NULL,
                ADD COLUMN deleted_at timestamptz NULL");

        // Nothing sets is_deleted today, but a row that has it becomes a tombstone like the new ones.
        Execute.Sql("UPDATE messages SET deleted_at = created_at, text = '' WHERE is_deleted");
        Execute.Sql("ALTER TABLE messages DROP COLUMN is_deleted");

        Execute.Sql(@"
            CREATE TABLE hidden_messages (
                user_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                message_id uuid NOT NULL REFERENCES messages (id) ON DELETE CASCADE,
                created_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, message_id)
            )");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS hidden_messages");
        Execute.Sql("ALTER TABLE messages ADD COLUMN is_deleted boolean NOT NULL DEFAULT false");
        Execute.Sql("UPDATE messages SET is_deleted = true WHERE deleted_at IS NOT NULL");
        Execute.Sql("ALTER TABLE messages DROP COLUMN deleted_at, DROP COLUMN edited_at, DROP COLUMN type");
    }
}
