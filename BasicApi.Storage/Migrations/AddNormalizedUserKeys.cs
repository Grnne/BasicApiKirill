using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Case-insensitive usernames and emails. "Alice" and "alice" used to be two
/// different accounts, and login depended on typing the exact case.
///
/// username_normalized / email_normalized are generated columns (lower(...)),
/// so no code path can forget to fill them; unique indexes on them make the
/// database the single source of truth for "is this name taken".
///
/// If the existing data already has case-only duplicates the migration stops
/// with a report of them instead of picking a winner itself: which account is
/// the real one is a human decision (rename or merge, then deploy again).
/// </summary>
[Migration(7)]
public class AddNormalizedUserKeys : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            DO $$
            DECLARE
                usernames text;
                emails text;
            BEGIN
                SELECT string_agg(k, ', ') INTO usernames FROM (
                    SELECT lower(username) AS k FROM users GROUP BY lower(username) HAVING COUNT(*) > 1
                ) d;
                SELECT string_agg(k, ', ') INTO emails FROM (
                    SELECT lower(email) AS k FROM users GROUP BY lower(email) HAVING COUNT(*) > 1
                ) d;
                IF usernames IS NOT NULL OR emails IS NOT NULL THEN
                    RAISE EXCEPTION 'Case-insensitive duplicates must be resolved before migration 7. Usernames: [%]. Emails: [%].',
                        coalesce(usernames, ''), coalesce(emails, '');
                END IF;
            END $$");

        Execute.Sql(@"
            ALTER TABLE users
                ADD COLUMN username_normalized text GENERATED ALWAYS AS (lower(username)) STORED,
                ADD COLUMN email_normalized text GENERATED ALWAYS AS (lower(email)) STORED");

        Execute.Sql("CREATE UNIQUE INDEX ux_users_username_normalized ON users (username_normalized)");
        Execute.Sql("CREATE UNIQUE INDEX ux_users_email_normalized ON users (email_normalized)");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ux_users_email_normalized");
        Execute.Sql("DROP INDEX IF EXISTS ux_users_username_normalized");
        Execute.Sql("ALTER TABLE users DROP COLUMN IF EXISTS email_normalized, DROP COLUMN IF EXISTS username_normalized");
    }
}
