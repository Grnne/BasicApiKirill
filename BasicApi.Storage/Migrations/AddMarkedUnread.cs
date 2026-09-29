using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// "Mark as unread" (plan 2, F2.2, D9): the user's own reminder on a chat, independent of the
/// unread counter. Reading the chat clears it.
/// </summary>
[Migration(18)]
public class AddMarkedUnread : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE chat_members ADD COLUMN marked_unread boolean NOT NULL DEFAULT false");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE chat_members DROP COLUMN marked_unread");
    }
}
