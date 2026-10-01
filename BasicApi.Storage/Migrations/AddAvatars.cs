using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Avatars of users and groups: a photo from <c>attachments</c>, like any other
/// file. Removing the file only clears the avatar.
/// </summary>
[Migration(25)]
public class AddAvatars : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE users ADD COLUMN avatar_attachment_id uuid NULL REFERENCES attachments (id) ON DELETE SET NULL");
        Execute.Sql("ALTER TABLE chats ADD COLUMN avatar_attachment_id uuid NULL REFERENCES attachments (id) ON DELETE SET NULL");
        // Who shows a file as an avatar: the access check and the cleanup look it up by the file.
        Execute.Sql("CREATE INDEX ix_users_avatar ON users (avatar_attachment_id) WHERE avatar_attachment_id IS NOT NULL");
        Execute.Sql("CREATE INDEX ix_chats_avatar ON chats (avatar_attachment_id) WHERE avatar_attachment_id IS NOT NULL");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE chats DROP COLUMN IF EXISTS avatar_attachment_id");
        Execute.Sql("ALTER TABLE users DROP COLUMN IF EXISTS avatar_attachment_id");
    }
}
