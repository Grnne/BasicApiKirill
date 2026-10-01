using FluentMigrator;

namespace BasicApi.Storage.Migrations;

[Migration(2)]
public class AddFullTextSearch : Migration
{
    public override void Up()
    {
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
