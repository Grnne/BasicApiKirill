using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Порядковый номер сообщения в чате и идемпотентная отправка.
///
/// messages.seq — номер внутри чата (1, 2, 3, …), уникален вместе с chat_id.
/// Выдаётся из chats.last_seq в транзакции вставки: строка чата блокируется, и
/// отправки в один чат получают номера строго по порядку фиксации. Порядок по seq
/// однозначен, в отличие от created_at, у которого бывают совпадения.
/// Существующие сообщения нумеруются по (created_at, id) — в том же порядке, в каком
/// их отдавала пагинация.
///
/// messages.client_message_id — id, который клиент выбирает сам до отправки.
/// Уникален вместе с sender_id: повтор отправки (ретрай после обрыва сети) находит
/// уже созданное сообщение вместо второго.
///
/// chat_members.last_read_seq вместо last_read_message_id: «прочитано до номера N».
/// Непрочитанные — простое сравнение номеров, без подзапроса к опорному сообщению.
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
