using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Privacy settings (<c>everybody</c>, <c>contacts</c> — those who share a chat, or <c>nobody</c>; no row means
/// everybody), blocks per pair and direction, and <c>last_seen_at</c> that survives restarts.
/// </summary>
[Migration(26)]
public class AddPrivacy : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE user_privacy (
                user_id uuid PRIMARY KEY REFERENCES users (id) ON DELETE CASCADE,
                last_seen text NOT NULL DEFAULT 'everybody' CHECK (last_seen IN ('everybody', 'contacts', 'nobody')),
                messages text NOT NULL DEFAULT 'everybody' CHECK (messages IN ('everybody', 'contacts', 'nobody')),
                group_add text NOT NULL DEFAULT 'everybody' CHECK (group_add IN ('everybody', 'contacts', 'nobody')),
                updated_at timestamptz NOT NULL
            )");
        Execute.Sql(@"
            CREATE TABLE user_blocks (
                blocker_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                blocked_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                created_at timestamptz NOT NULL,
                PRIMARY KEY (blocker_id, blocked_id),
                CHECK (blocker_id <> blocked_id)
            )");
        // "Who blocked me" — checked on every private message and presence broadcast.
        Execute.Sql("CREATE INDEX ix_user_blocks_blocked ON user_blocks (blocked_id)");
        Execute.Sql("ALTER TABLE users ADD COLUMN last_seen_at timestamptz NULL");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE users DROP COLUMN IF EXISTS last_seen_at");
        Execute.Sql("DROP TABLE IF EXISTS user_blocks");
        Execute.Sql("DROP TABLE IF EXISTS user_privacy");
    }
}
