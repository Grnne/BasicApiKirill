using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Message search with the 'russian' config (Latin words still get the English stemmer). Queries must use
/// the same config, otherwise stems will not match. Adding the STORED column rewrites the messages table.
/// </summary>
[Migration(12)]
public class AddRussianMessageSearch : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE messages
            ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (to_tsvector('russian', text)) STORED");
        Execute.Sql("CREATE INDEX ix_messages_search_vector ON messages USING GIN (search_vector)");
        Execute.Sql("DROP INDEX IF EXISTS ix_messages_search_gin");
    }

    public override void Down()
    {
        Execute.Sql("CREATE INDEX IF NOT EXISTS ix_messages_search_gin ON messages USING GIN (to_tsvector('english', text))");
        Execute.Sql("DROP INDEX IF EXISTS ix_messages_search_vector");
        Execute.Sql("ALTER TABLE messages DROP COLUMN search_vector");
    }
}
