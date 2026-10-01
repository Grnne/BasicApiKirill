using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// All time columns become timestamptz: Npgsql sends UTC as timestamptz, and a timestamp column shifted it
/// to the session time zone. Existing values are read as UTC (that is how the app wrote them).
/// ALTER TYPE rewrites the tables under an exclusive lock: on a large database it is a maintenance window.
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
            // Not now() at time zone 'utc': that yields timestamp without zone and depends on the session zone again.
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
