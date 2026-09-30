using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Groups (plan 2, F3, D8).
///
/// chats: who created the group, when it was last changed, and its settings — for now the
/// default permissions of members. chat_members: the role (owner / admin / member) and per-member
/// overrides of permissions; one owner per chat. messages.content: what a system message is about
/// ("members added" and the like). chat_audit_log: the group's actions, for admins.
///
/// Groups made before this (directly in the database) get an owner: their earliest member.
/// </summary>
[Migration(20)]
public class AddGroups : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE chats
                ADD COLUMN created_by uuid NULL REFERENCES users (id) ON DELETE SET NULL,
                ADD COLUMN updated_at timestamptz NULL,
                ADD COLUMN settings jsonb NULL");

        Execute.Sql(@"
            ALTER TABLE chat_members
                ADD COLUMN role text NOT NULL DEFAULT 'member' CHECK (role IN ('owner', 'admin', 'member')),
                ADD COLUMN permissions jsonb NULL");

        Execute.Sql(@"
            UPDATE chat_members cm SET role = 'owner'
            FROM (
                SELECT DISTINCT ON (m.chat_id) m.chat_id, m.user_id
                FROM chat_members m
                JOIN chats c ON c.id = m.chat_id AND c.type = 'group'
                ORDER BY m.chat_id, m.joined_at, m.user_id
            ) first
            WHERE cm.chat_id = first.chat_id AND cm.user_id = first.user_id");

        Execute.Sql("CREATE UNIQUE INDEX ux_chat_members_owner ON chat_members (chat_id) WHERE role = 'owner'");

        Execute.Sql("ALTER TABLE messages ADD COLUMN content jsonb NULL");

        Execute.Sql(@"
            CREATE TABLE chat_audit_log (
                id bigserial PRIMARY KEY,
                chat_id uuid NOT NULL REFERENCES chats (id) ON DELETE CASCADE,
                actor_id uuid NULL REFERENCES users (id) ON DELETE SET NULL,
                action text NOT NULL,
                target_user_id uuid NULL REFERENCES users (id) ON DELETE SET NULL,
                data jsonb NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            )");
        Execute.Sql("CREATE INDEX ix_chat_audit_log_chat_id ON chat_audit_log (chat_id, id DESC)");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS chat_audit_log");
        Execute.Sql("ALTER TABLE messages DROP COLUMN content");
        Execute.Sql("DROP INDEX IF EXISTS ux_chat_members_owner");
        Execute.Sql("ALTER TABLE chat_members DROP COLUMN permissions, DROP COLUMN role");
        Execute.Sql("ALTER TABLE chats DROP COLUMN settings, DROP COLUMN updated_at, DROP COLUMN created_by");
    }
}
