using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Replies (always within the same chat) and forwards; a forward of a forward keeps the original.
/// All links are SET NULL: losing the original must not take the copy with it.
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
