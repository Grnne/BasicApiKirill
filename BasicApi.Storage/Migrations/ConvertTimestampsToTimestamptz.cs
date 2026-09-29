using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// All time columns are timestamptz (in sessions it was like that from the start).
///
/// The app wrote UTC into timestamp without zone, but Npgsql passes UTC time as
/// timestamptz, and on write Postgres converted it to the session time zone. When the
/// server zone is not UTC, message times were shifted; they reached the client without a zone marker,
/// and the browser read them as local time.
///
/// Existing values are treated as UTC - that is how the app wrote them. ALTER TYPE
/// rewrites the tables and holds an exclusive lock on them: on a large database it is
/// a maintenance window (see docs/deploy.md).
///
/// Default values are now(): the previous (now() at time zone 'utc') yields timestamp
/// without zone and, after the type change, would again depend on the session time zone.
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
