using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// The journal pts each user device has reached. A device is a sign-in
/// (a session chain, sid in the token). Plan 2 builds the "delivered" status on this.
/// </summary>
[Migration(11)]
public class AddUserSyncState : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE user_sync_state (
                user_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                session_family_id uuid NOT NULL,
                acked_pts bigint NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, session_family_id)
            )");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS user_sync_state");
    }
}
