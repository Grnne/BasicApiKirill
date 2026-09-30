using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Files in messages (plan 2, F4.2): a message points to up to 10 files, in order (an album).
/// A forward points to the same files — nothing is uploaded again. The chat and the message's
/// number are copied here, so "the chat's photos" is one index range, not a scan of all messages
/// (the gallery, F4.3). A file in a message cannot be deleted from under it.
/// </summary>
[Migration(23)]
public class AddMessageAttachments : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE message_attachments (
                message_id uuid NOT NULL REFERENCES messages (id) ON DELETE CASCADE,
                position smallint NOT NULL,
                attachment_id uuid NOT NULL REFERENCES attachments (id),
                chat_id uuid NOT NULL,
                seq bigint NOT NULL,
                kind text NOT NULL,
                PRIMARY KEY (message_id, position)
            )");
        Execute.Sql("CREATE INDEX ix_message_attachments_attachment ON message_attachments (attachment_id)");
        Execute.Sql("CREATE INDEX ix_message_attachments_chat ON message_attachments (chat_id, kind, seq DESC)");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS message_attachments");
    }
}
