using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Drafts (plan 2, F2.3, D4): one per user and chat, shared by the user's devices. Tied to the
/// membership row: leaving the chat drops the draft. A reply target deleted from the database
/// only unlinks the draft from it.
/// </summary>
[Migration(19)]
public class AddUserDrafts : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE user_drafts (
                user_id uuid NOT NULL,
                chat_id uuid NOT NULL,
                text text NOT NULL,
                entities jsonb NULL,
                reply_to_message_id uuid NULL REFERENCES messages (id) ON DELETE SET NULL,
                updated_at timestamptz NOT NULL,
                PRIMARY KEY (user_id, chat_id),
                FOREIGN KEY (chat_id, user_id) REFERENCES chat_members (chat_id, user_id) ON DELETE CASCADE
            )");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS user_drafts");
    }
}
