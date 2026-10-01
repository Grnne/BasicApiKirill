using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Refresh-token sessions; all rotations of one login share a family_id, so a reused token revokes the chain.
/// Only token hashes are stored: a database dump must not hand out working sessions.
/// </summary>
[Migration(4)]
public class AddSessions : Migration
{
    public override void Up()
    {
        Create.Table("sessions")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("user_id").AsGuid().NotNullable()
            .WithColumn("family_id").AsGuid().NotNullable()
            .WithColumn("refresh_token_hash").AsString(64).NotNullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("expires_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("revoked_at").AsCustom("timestamptz").Nullable()
            .WithColumn("replaced_by_session_id").AsGuid().Nullable()
            .WithColumn("user_agent").AsString(400).Nullable()
            .WithColumn("ip").AsString(64).Nullable();

        Create.ForeignKey("FK_Sessions_Users")
            .FromTable("sessions").ForeignColumn("user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        // Lookup by presented refresh token — unique so a hash collision or a
        // double insert surfaces as a constraint violation instead of ambiguity.
        Execute.Sql(@"
            CREATE UNIQUE INDEX ix_sessions_refresh_token_hash
            ON sessions (refresh_token_hash)");

        // "Revoke every session of this user" (logout everywhere, deactivation).
        Execute.Sql(@"
            CREATE INDEX ix_sessions_user_id
            ON sessions (user_id)
            WHERE revoked_at IS NULL");

        // Family revocation on detected refresh-token reuse
        Execute.Sql(@"
            CREATE INDEX ix_sessions_family_id
            ON sessions (family_id)
            WHERE revoked_at IS NULL");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS ix_sessions_family_id");
        Execute.Sql("DROP INDEX IF EXISTS ix_sessions_user_id");
        Execute.Sql("DROP INDEX IF EXISTS ix_sessions_refresh_token_hash");
        Delete.ForeignKey("FK_Sessions_Users").OnTable("sessions");
        Delete.Table("sessions");
    }
}
