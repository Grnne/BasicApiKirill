using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// One-time invitations to register, when registration is by invitation: a member makes one and
/// passes the code on. Only the code's hash is kept, as with refresh tokens.
/// </summary>
[Migration(33)]
public class AddInvites : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE invites (
                code_hash text PRIMARY KEY,
                created_by uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                created_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL,
                used_by uuid NULL REFERENCES users (id) ON DELETE SET NULL,
                used_at timestamptz NULL
            )");
        Execute.Sql("CREATE INDEX ix_invites_created_by ON invites (created_by, expires_at)");
        Execute.Sql("CREATE INDEX ix_invites_used_by ON invites (used_by) WHERE used_by IS NOT NULL");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS invites");
    }
}
