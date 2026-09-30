using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// The "links" tab of a chat's gallery (plan 2, F4.3): whether a message has a web address in its
/// text or a link in its formatting — computed by the database, so edits keep it right. A partial
/// index keeps the tab one index range. Rewrites <c>messages</c>, like migration 12.
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
