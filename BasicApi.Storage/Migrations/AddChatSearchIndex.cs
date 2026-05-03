using FluentMigrator;

namespace BasicApi.Storage.Migrations;

[Migration(3)]
public class AddChatSearchIndex : Migration
{
    public override void Up()
    {
        // Enable pg_trgm extension for trigram-based ILIKE search
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm");

        // GIN trigram index for fast ILIKE search on chats.title
        // pg_trgm enables fast pattern matching for GROUP chat title searches:
        //   WHERE c.type = 'group' AND c.title ILIKE '%query%'
        Execute.Sql(@"
            CREATE INDEX IF NOT EXISTS ix_chats_title_trgm
            ON chats
            USING GIN (title gin_trgm_ops)
            WHERE type = 'group'");

        // GIN trigram index for fast ILIKE search on users.display_name and users.username
        // Enables fast companion search for PRIVATE chats:
        //   WHERE comp.display_name ILIKE '%query%' OR comp.username ILIKE '%query%'
        Execute.Sql(@"
            CREATE INDEX IF NOT EXISTS ix_users_display_name_trgm
            ON users
            USING GIN (display_name gin_trgm_ops)");

        Execute.Sql(@"
            CREATE INDEX IF NOT EXISTS ix_users_username_trgm
            ON users
            USING GIN (username gin_trgm_ops)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_chats_title_trgm");
        Execute.Sql("DROP INDEX IF EXISTS ix_users_display_name_trgm");
        Execute.Sql("DROP INDEX IF EXISTS ix_users_username_trgm");
    }
}

