using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Все временные колонки — timestamptz (в sessions так было с самого начала).
///
/// В timestamp без зоны приложение писало UTC, но Npgsql передаёт UTC-время как
/// timestamptz, и при записи Postgres переводил его в часовой пояс сессии. При поясе
/// сервера не UTC время сообщений сдвигалось; клиенту оно уходило без признака зоны,
/// и браузер читал его как местное.
///
/// Существующие значения считаются UTC — так их и писало приложение. ALTER TYPE
/// переписывает таблицы и держит на них эксклюзивную блокировку: на большой базе —
/// окно обслуживания (см. docs/deploy.md).
///
/// Значения по умолчанию — now(): прежнее (now() at time zone 'utc') даёт timestamp
/// без зоны и после смены типа снова зависело бы от пояса сессии.
/// </summary>
[Migration(8)]
public class ConvertTimestampsToTimestamptz : Migration
{
    private static readonly (string Table, string Column, bool HasDefault)[] Columns =
    [
        ("users", "created_at", true),
        ("users", "last_login_at", false),
        ("chats", "created_at", true),
        ("chat_members", "joined_at", true),
        ("messages", "created_at", true),
    ];

    public override void Up()
    {
        foreach (var (table, column, hasDefault) in Columns)
        {
            Execute.Sql($"ALTER TABLE {table} ALTER COLUMN {column} TYPE timestamptz USING {column} AT TIME ZONE 'UTC'");
            if (hasDefault)
                Execute.Sql($"ALTER TABLE {table} ALTER COLUMN {column} SET DEFAULT now()");
        }
    }

    public override void Down()
    {
        foreach (var (table, column, hasDefault) in Columns)
        {
            Execute.Sql($"ALTER TABLE {table} ALTER COLUMN {column} TYPE timestamp USING {column} AT TIME ZONE 'UTC'");
            if (hasDefault)
                Execute.Sql($"ALTER TABLE {table} ALTER COLUMN {column} SET DEFAULT (now() at time zone 'utc')");
        }
    }
}
