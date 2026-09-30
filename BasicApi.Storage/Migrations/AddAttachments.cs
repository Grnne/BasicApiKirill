using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// Files (plan 2, F4.1, A3, D2). A file lives apart from messages: messages, avatars and forwards
/// only point to it. The object itself is in S3 storage under <c>storage_key</c>; the row keeps
/// what the server checked after the upload (size, sha256, type, picture size) and where the
/// file is: <c>pending</c> — waiting for the upload, <c>stored</c>, <c>expired</c> — the original
/// was removed by the retention policy, the preview is kept.
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
