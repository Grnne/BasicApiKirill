using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Replies and forwards (plan 2, F1.2).
///
/// reply_to_message_id — the message being answered, always in the same chat.
/// forward_from_* — where a forwarded message originally came from: author, chat and message.
/// A forward of a forward keeps the original, as in Telegram. All links are SET NULL: losing
/// the original must not take the copy with it.
/// </summary>
[Migration(14)]
public class AddMessageReplyAndForward : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE messages
                ADD COLUMN reply_to_message_id uuid NULL REFERENCES messages (id) ON DELETE SET NULL,
                ADD COLUMN forward_from_user_id uuid NULL REFERENCES users (id) ON DELETE SET NULL,
                ADD COLUMN forward_from_chat_id uuid NULL REFERENCES chats (id) ON DELETE SET NULL,
                ADD COLUMN forward_from_message_id uuid NULL REFERENCES messages (id) ON DELETE SET NULL");
    }

    public override void Down()
    {
        Execute.Sql(@"
            ALTER TABLE messages
                DROP COLUMN forward_from_message_id,
                DROP COLUMN forward_from_chat_id,
                DROP COLUMN forward_from_user_id,
                DROP COLUMN reply_to_message_id");
    }
}
