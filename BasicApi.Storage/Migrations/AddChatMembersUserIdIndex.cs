using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// "All chats of a user" (chat list, presence, hub connect): the (chat_id, user_id) key cannot serve it.
/// CONCURRENTLY does not block writes but cannot run in a transaction, hence TransactionBehavior.None.
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
