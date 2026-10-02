using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// The status of one's messages is how far the others got. A member who joins starts at the
/// chat's last message without having read it: joined_seq marks where their reading starts to
/// count. A member who leaves takes their pointers along: the chat keeps the furthest of them, so
/// a message read by someone who left stays read.
/// </summary>
[Migration(32)]
public class AddReadStatusBounds : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE chat_members ADD COLUMN joined_seq bigint NOT NULL DEFAULT 0");
        Execute.Sql(@"
            ALTER TABLE chats
                ADD COLUMN departed_read_seq bigint NOT NULL DEFAULT 0,
                ADD COLUMN departed_delivered_seq bigint NOT NULL DEFAULT 0");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE chats DROP COLUMN departed_read_seq, DROP COLUMN departed_delivered_seq");
        Execute.Sql("ALTER TABLE chat_members DROP COLUMN joined_seq");
    }
}
