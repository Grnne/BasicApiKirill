using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// "Delivered" status (plan 2, F2.1, D1): the last seq in the chat that at least one device
/// of the member has received and acknowledged through /sync/ack. What is read has been
/// delivered too, so the pointer starts at the read one and never falls behind it.
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
