using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// entities: text formatting (offset and length in UTF-16 code units). message_mentions: who is mentioned where;
/// chat_id and seq are copied from the message so the unread-mentions count needs no join.
/// </summary>
[Migration(15)]
public class AddMessageEntitiesAndMentions : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE messages ADD COLUMN entities jsonb NULL");

        Execute.Sql(@"
            CREATE TABLE message_mentions (
                message_id uuid NOT NULL REFERENCES messages (id) ON DELETE CASCADE,
                user_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                chat_id uuid NOT NULL,
                seq bigint NOT NULL,
                PRIMARY KEY (message_id, user_id)
            )");
        Execute.Sql("CREATE INDEX ix_message_mentions_user_chat_seq ON message_mentions (user_id, chat_id, seq)");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS message_mentions");
        Execute.Sql("ALTER TABLE messages DROP COLUMN entities");
    }
}
