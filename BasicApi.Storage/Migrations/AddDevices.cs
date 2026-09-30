using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Devices (plan 2, F7.1, A4). A device is one sign-in: the chain of refresh sessions that one
/// login starts (<c>sid</c> in the token), so the id is that chain's id. The row appears together
/// with the first session of the chain; push subscriptions hang off it. Sign-ins made before
/// this migration get their row here.
/// </summary>
[Migration(29)]
public class AddDevices : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE devices (
                id uuid PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                created_at timestamptz NOT NULL
            )");
        Execute.Sql("CREATE INDEX ix_devices_user ON devices (user_id)");
        Execute.Sql(@"
            INSERT INTO devices (id, user_id, created_at)
            SELECT DISTINCT ON (family_id) family_id, user_id,
                   min(created_at) OVER (PARTITION BY family_id)
            FROM sessions s
            WHERE EXISTS (
                SELECT 1 FROM sessions l
                WHERE l.family_id = s.family_id AND l.revoked_at IS NULL AND l.expires_at > now()
            )");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS devices");
    }
}
