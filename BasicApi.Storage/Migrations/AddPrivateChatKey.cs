using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// One private chat per pair: private_key = "{smaller uuid}:{larger uuid}" under a unique index.
/// Existing duplicates are merged into the oldest chat of the pair first.
/// </summary>
[Migration(6)]
public class AddPrivateChatKey : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE chats ADD COLUMN private_key text NULL");

        // Participant pairs of private chats (valid ones only: exactly two participants).
        Execute.Sql(@"
            CREATE TEMP TABLE private_pairs ON COMMIT DROP AS
            SELECT c.id, c.created_at,
                   LEAST(m1.user_id, m2.user_id)::text || ':' || GREATEST(m1.user_id, m2.user_id)::text AS pair_key
            FROM chats c
            JOIN chat_members m1 ON m1.chat_id = c.id
            JOIN chat_members m2 ON m2.chat_id = c.id AND m2.user_id > m1.user_id
            WHERE c.type = 'private'
              AND (SELECT COUNT(*) FROM chat_members x WHERE x.chat_id = c.id) = 2");

        // For each pair the oldest chat stays; the rest are duplicates.
        Execute.Sql(@"
            CREATE TEMP TABLE private_dups ON COMMIT DROP AS
            SELECT id AS dup_id, keeper
            FROM (
                SELECT id, first_value(id) OVER (PARTITION BY pair_key ORDER BY created_at, id) AS keeper
                FROM private_pairs
            ) ranked
            WHERE id <> keeper");

        Execute.Sql(@"
            UPDATE messages m
            SET chat_id = d.keeper
            FROM private_dups d
            WHERE m.chat_id = d.dup_id");

        // The read pointer is the latest of the participant's pointers across all duplicates.
        Execute.Sql(@"
            UPDATE chat_members k
            SET last_read_message_id = (
                SELECT msg.id
                FROM chat_members x
                JOIN messages msg ON msg.id = x.last_read_message_id
                WHERE x.user_id = k.user_id
                  AND (x.chat_id = k.chat_id
                       OR x.chat_id IN (SELECT dup_id FROM private_dups WHERE keeper = k.chat_id))
                ORDER BY msg.created_at DESC, msg.id DESC
                LIMIT 1)
            WHERE k.chat_id IN (SELECT keeper FROM private_dups)");

        Execute.Sql("DELETE FROM chats WHERE id IN (SELECT dup_id FROM private_dups)");

        Execute.Sql(@"
            UPDATE chats c
            SET private_key = p.pair_key
            FROM private_pairs p
            WHERE p.id = c.id");

        Execute.Sql("CREATE UNIQUE INDEX ux_chats_private_key ON chats (private_key)");
    }

    public override void Down()
    {
        // Merged duplicates are not restored: they were the mistake in the first place.
        Execute.Sql("DROP INDEX IF EXISTS ux_chats_private_key");
        Execute.Sql("ALTER TABLE chats DROP COLUMN IF EXISTS private_key");
    }
}
