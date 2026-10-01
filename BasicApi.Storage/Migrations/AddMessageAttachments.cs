using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// A message points to up to 10 files in order (an album); a forward reuses them. chat_id and seq are copied
/// so the chat's gallery is one index range. A file in a message cannot be deleted from under it.
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
