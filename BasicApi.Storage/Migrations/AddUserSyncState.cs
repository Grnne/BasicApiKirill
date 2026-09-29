using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// До какого pts журнала дошло каждое устройство пользователя. Устройство — это вход
/// (цепочка сессий, sid в токене). На этом в плане 2 строится статус «доставлено».
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
