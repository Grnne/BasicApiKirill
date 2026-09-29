using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Надёжная доставка событий.
///
/// outbox — события, которые ещё надо разослать через SignalR. Пишутся в той же
/// транзакции, что и изменение, которое их породило: сообщение сохранено — значит,
/// и событие о нём сохранено. Фоновый диспетчер рассылает и отмечает processed_at.
/// Падение процесса между сохранением и рассылкой больше не теряет событие.
///
/// user_updates — журнал изменений каждого пользователя с его собственной нумерацией
/// pts (1, 2, 3, …, счётчик — user_pts). Клиент, пропустивший события (офлайн,
/// реконнект), догоняет по журналу: «всё после моего pts». Эфемерное — «печатает»,
/// онлайн — в журнал не пишется.
/// </summary>
[Migration(10)]
public class AddOutboxAndUpdateJournal : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE outbox (
                id bigserial PRIMARY KEY,
                type text NOT NULL,
                payload jsonb NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                processed_at timestamptz NULL,
                attempts int NOT NULL DEFAULT 0
            )");
        Execute.Sql("CREATE INDEX ix_outbox_pending ON outbox (id) WHERE processed_at IS NULL");

        Execute.Sql(@"
            CREATE TABLE user_pts (
                user_id uuid PRIMARY KEY REFERENCES users (id) ON DELETE CASCADE,
                last_pts bigint NOT NULL
            )");

        Execute.Sql(@"
            CREATE TABLE user_updates (
                user_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                pts bigint NOT NULL,
                type text NOT NULL,
                payload jsonb NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, pts)
            )");
        Execute.Sql("CREATE INDEX ix_user_updates_created_at ON user_updates (created_at)");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS user_updates");
        Execute.Sql("DROP TABLE IF EXISTS user_pts");
        Execute.Sql("DROP TABLE IF EXISTS outbox");
    }
}
