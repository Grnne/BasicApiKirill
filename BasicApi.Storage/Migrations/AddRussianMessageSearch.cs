using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Message search with the Russian dictionary.
///
/// Previously both the index and the queries used 'english': Russian words were not reduced to
/// a stem, so "запуск" did not find "запускаем". The 'russian'
/// configuration parses Russian words with the Russian stemmer and Latin words with the English
/// one, so mixed messages are searchable in both languages.
///
/// search_vector is a generated column: it need not be filled in code, and a query
/// does not recompute to_tsvector for every row. Queries must use the same
/// configuration ('russian'), otherwise word stems will not match.
///
/// Adding a STORED column rewrites the messages table (see docs/deploy.md).
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
