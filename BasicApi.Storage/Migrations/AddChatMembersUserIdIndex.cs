using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Index for "all chats of a user" — the chat list, presence fan-out and hub
/// connect all start from chat_members by user_id. The primary key
/// (chat_id, user_id) cannot serve that lookup, so without this index every
/// such query scans the whole table.
///
/// CONCURRENTLY does not lock writes on a live database, but cannot run inside
/// a transaction — hence TransactionBehavior.None.
/// </summary>
[Migration(5, TransactionBehavior.None)]
public class AddChatMembersUserIdIndex : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_chat_members_user_id
            ON chat_members (user_id)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_chat_members_user_id");
    }
}
