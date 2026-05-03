using FluentMigrator;

namespace BasicApi.Storage.Migrations;

[Migration(2)]
public class AddFullTextSearch : Migration
{
    public override void Up()
    {
        // GIN index for full-text search on messages.text using PostgreSQL tsvector.
        // This enables fast full-text search via:
        //   WHERE to_tsvector('english', text) @@ plainto_tsquery('english', @query)
        //
        // GIN (Generalized Inverted Index) is PostgreSQL's equivalent of an inverted index,
        // similar to what Telegram uses for search — but built-in and maintained automatically.
        Execute.Sql(@"
            CREATE INDEX IF NOT EXISTS ix_messages_search_gin
            ON messages
            USING GIN (to_tsvector('english', text))");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_messages_search_gin");
    }
}
