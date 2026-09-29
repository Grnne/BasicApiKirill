using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Sequence number of a message in a chat, and idempotent sending.
///
/// messages.seq is the number within a chat (1, 2, 3, ...), unique together with chat_id.
/// It is issued from chats.last_seq in the insert transaction: the chat row is locked, so
/// sends to one chat get numbers strictly in commit order. Ordering by seq is unambiguous,
/// unlike created_at, which can have ties. Existing messages are numbered by (created_at, id),
/// the same order in which pagination returned them.
///
/// messages.client_message_id is an id the client picks itself before sending.
/// Unique together with sender_id: a resend (retry after a network drop) finds the message
/// already created instead of creating a second one.
///
/// chat_members.last_read_seq instead of last_read_message_id: "read up to number N".
/// Unread is a simple number comparison, with no subquery to the anchor message.
/// </summary>
[Migration(9)]
public class AddMessageSeq : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE chats ADD COLUMN last_seq bigint NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE messages ADD COLUMN seq bigint NULL");
        Execute.Sql("ALTER TABLE messages ADD COLUMN client_message_id uuid NULL");

        Execute.Sql(@"
            UPDATE messages m SET seq = n.seq
            FROM (
                SELECT id, row_number() OVER (PARTITION BY chat_id ORDER BY created_at, id) AS seq
                FROM messages
            ) n
            WHERE n.id = m.id");
        Execute.Sql("ALTER TABLE messages ALTER COLUMN seq SET NOT NULL");
        Execute.Sql("CREATE UNIQUE INDEX ux_messages_chat_id_seq ON messages (chat_id, seq)");
        Execute.Sql(@"
            CREATE UNIQUE INDEX ux_messages_sender_id_client_message_id
            ON messages (sender_id, client_message_id)
            WHERE client_message_id IS NOT NULL");

        Execute.Sql(@"
            UPDATE chats c SET last_seq = m.max_seq
            FROM (SELECT chat_id, MAX(seq) AS max_seq FROM messages GROUP BY chat_id) m
            WHERE m.chat_id = c.id");

        Execute.Sql("ALTER TABLE chat_members ADD COLUMN last_read_seq bigint NOT NULL DEFAULT 0");
        Execute.Sql(@"
            UPDATE chat_members cm SET last_read_seq = m.seq
            FROM messages m
            WHERE m.id = cm.last_read_message_id AND m.chat_id = cm.chat_id");
        Execute.Sql("ALTER TABLE chat_members DROP COLUMN last_read_message_id");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE chat_members ADD COLUMN last_read_message_id uuid NULL");
        Execute.Sql(@"
            UPDATE chat_members cm SET last_read_message_id = m.id
            FROM messages m
            WHERE m.chat_id = cm.chat_id AND m.seq = cm.last_read_seq");
        Execute.Sql("ALTER TABLE chat_members DROP COLUMN last_read_seq");

        Execute.Sql("DROP INDEX IF EXISTS ux_messages_sender_id_client_message_id");
        Execute.Sql("DROP INDEX IF EXISTS ux_messages_chat_id_seq");
        Execute.Sql("ALTER TABLE messages DROP COLUMN client_message_id");
        Execute.Sql("ALTER TABLE messages DROP COLUMN seq");
        Execute.Sql("ALTER TABLE chats DROP COLUMN last_seq");
    }
}
