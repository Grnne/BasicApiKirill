using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// has_links for the gallery's "links" tab, computed by the database so edits keep it right; a partial index
/// keeps the tab one index range. Rewrites <c>messages</c>.
/// </summary>
[Migration(24)]
public class AddMessageLinksFlag : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE messages ADD COLUMN has_links boolean GENERATED ALWAYS AS (
                text ~* 'https?://' OR COALESCE(entities @> '[{""type"": ""link""}]'::jsonb, false)
            ) STORED");
        Execute.Sql(@"
            CREATE INDEX ix_messages_links ON messages (chat_id, seq DESC)
            WHERE has_links AND deleted_at IS NULL");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_messages_links");
        Execute.Sql("ALTER TABLE messages DROP COLUMN IF EXISTS has_links");
    }
}
