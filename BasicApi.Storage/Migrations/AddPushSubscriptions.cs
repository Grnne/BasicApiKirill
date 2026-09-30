using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// A WebPush subscription per device (plan 2, F7.2): it goes away with the device. One browser
/// subscription belongs to one device only — whoever signs in there next takes it over.
/// </summary>
[Migration(30)]
public class AddPushSubscriptions : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE devices
                ADD COLUMN push_endpoint text NULL,
                ADD COLUMN push_p256dh text NULL,
                ADD COLUMN push_auth text NULL,
                ADD COLUMN push_updated_at timestamptz NULL");
        Execute.Sql(@"
            CREATE UNIQUE INDEX ux_devices_push_endpoint ON devices (push_endpoint)
            WHERE push_endpoint IS NOT NULL");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ux_devices_push_endpoint");
        Execute.Sql(@"
            ALTER TABLE devices
                DROP COLUMN IF EXISTS push_endpoint,
                DROP COLUMN IF EXISTS push_p256dh,
                DROP COLUMN IF EXISTS push_auth,
                DROP COLUMN IF EXISTS push_updated_at");
    }
}
