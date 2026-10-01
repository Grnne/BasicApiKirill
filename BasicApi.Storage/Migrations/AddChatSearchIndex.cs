using FluentMigrator;

namespace BasicApi.Storage.Migrations;

[Migration(3)]
public class AddChatSearchIndex : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm");

        // Trigram indexes serve ILIKE '%query%': group titles here, companion names of private chats below.
        Execute.Sql(@"
            CREATE INDEX IF NOT EXISTS ix_chats_title_trgm
            ON chats
            USING GIN (title gin_trgm_ops)
            WHERE type = 'group'");

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

