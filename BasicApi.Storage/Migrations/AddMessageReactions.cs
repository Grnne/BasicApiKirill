using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Reactions (plan 2, F1.4, D7).
///
/// message_reactions — one reaction per user per message, as in WhatsApp: a new one replaces
/// the old. messages.reactions_summary — [{emoji, count}], recomputed on every change, so pages
/// of history do not count reactions row by row.
/// </summary>
[Migration(16)]
public class AddMessageReactions : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE message_reactions (
                message_id uuid NOT NULL REFERENCES messages (id) ON DELETE CASCADE,
                user_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                emoji text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (message_id, user_id)
            )");

        Execute.Sql("ALTER TABLE messages ADD COLUMN reactions_summary jsonb NULL");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE messages DROP COLUMN reactions_summary");
        Execute.Sql("DROP TABLE IF EXISTS message_reactions");
    }
}
