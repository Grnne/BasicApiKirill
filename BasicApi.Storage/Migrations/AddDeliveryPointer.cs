using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// "Delivered" pointer: the last seq at least one of the member's devices acknowledged through /sync/ack.
/// What is read is delivered too, so it starts at the read pointer and never falls behind it.
/// </summary>
[Migration(17)]
public class AddDeliveryPointer : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE chat_members ADD COLUMN last_delivered_seq bigint NOT NULL DEFAULT 0");
        Execute.Sql("UPDATE chat_members SET last_delivered_seq = last_read_seq");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE chat_members DROP COLUMN last_delivered_seq");
    }
}
