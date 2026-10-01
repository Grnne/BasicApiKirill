using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Case-insensitive usernames and emails: generated lower(...) columns under unique indexes.
/// Existing case-only duplicates stop the migration with a report: which account is real is a human decision.
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
