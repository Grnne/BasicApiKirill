using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Reactions to a member's messages they have not seen yet, as unread mentions are: a reaction is
/// new while it is younger than chat_members.reactions_seen_at, which reading the chat moves on.
/// The reaction keeps its message's chat and author so the chat list counts them by index.
/// </summary>
[Migration(34)]
public class AddUnreadReactions : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE message_reactions
                ADD COLUMN chat_id uuid NULL,
                ADD COLUMN author_id uuid NULL");
        Execute.Sql(@"
            UPDATE message_reactions r SET chat_id = m.chat_id, author_id = m.sender_id
            FROM messages m WHERE m.id = r.message_id");
        Execute.Sql(@"
            ALTER TABLE message_reactions
                ALTER COLUMN chat_id SET NOT NULL,
                ALTER COLUMN author_id SET NOT NULL");
        Execute.Sql("CREATE INDEX ix_message_reactions_author ON message_reactions (author_id, chat_id, created_at)");

        // Reactions made before this migration count as seen.
        Execute.Sql("ALTER TABLE chat_members ADD COLUMN reactions_seen_at timestamptz NOT NULL DEFAULT now()");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE chat_members DROP COLUMN reactions_seen_at");
        Execute.Sql("DROP INDEX IF EXISTS ix_message_reactions_author");
        Execute.Sql("ALTER TABLE message_reactions DROP COLUMN author_id, DROP COLUMN chat_id");
    }
}
