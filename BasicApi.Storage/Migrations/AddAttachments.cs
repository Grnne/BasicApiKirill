using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Files, kept in S3 under <c>storage_key</c>; messages and avatars only point to them.
/// <c>expired</c>: the retention policy removed the original, the preview is kept.
/// </summary>
[Migration(22)]
public class AddAttachments : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE attachments (
                id uuid PRIMARY KEY,
                owner_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                kind text NOT NULL CHECK (kind IN ('photo', 'video', 'file', 'voice')),
                file_name text NOT NULL,
                mime text NOT NULL,
                size bigint NOT NULL,
                sha256 bytea NULL,
                width integer NULL,
                height integer NULL,
                duration_ms integer NULL,
                waveform bytea NULL,
                storage_key text NOT NULL,
                thumbnail_key text NULL,
                storage_state text NOT NULL DEFAULT 'pending'
                    CHECK (storage_state IN ('pending', 'stored', 'expired')),
                created_at timestamptz NOT NULL,
                stored_at timestamptz NULL
            )");

        // Unfinished uploads: counted per user and swept when they go stale.
        Execute.Sql("CREATE INDEX ix_attachments_pending ON attachments (owner_id, created_at) WHERE storage_state = 'pending'");
        Execute.Sql("CREATE INDEX ix_attachments_pending_age ON attachments (created_at) WHERE storage_state = 'pending'");
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE IF EXISTS attachments");
    }
}
