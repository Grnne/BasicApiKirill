using FluentMigrator;

namespace BasicApi.Storage.Migrations;

/// <summary>
/// An index under every foreign key. Deleting a chat cascades to its messages, and for each one
/// Postgres looks for the rows that reply to it, forward it or hide it — a full scan per message
/// without these. Most messages are neither replies nor forwards, so those indexes are partial.
/// CONCURRENTLY does not block writes but cannot run in a transaction, hence TransactionBehavior.None.
/// </summary>
[Migration(31, TransactionBehavior.None)]
public class AddForeignKeyIndexes : Migration
{
    private static readonly (string Name, string Definition)[] Indexes =
    [
        ("ix_messages_reply_to", "messages (reply_to_message_id) WHERE reply_to_message_id IS NOT NULL"),
        ("ix_messages_forward_from_message", "messages (forward_from_message_id) WHERE forward_from_message_id IS NOT NULL"),
        ("ix_messages_forward_from_chat", "messages (forward_from_chat_id) WHERE forward_from_chat_id IS NOT NULL"),
        ("ix_messages_forward_from_user", "messages (forward_from_user_id) WHERE forward_from_user_id IS NOT NULL"),
        ("ix_hidden_messages_message", "hidden_messages (message_id)"),
        ("ix_user_drafts_reply_to", "user_drafts (reply_to_message_id) WHERE reply_to_message_id IS NOT NULL"),
        ("ix_message_reactions_user", "message_reactions (user_id)"),
        ("ix_folder_chats_chat", "folder_chats (chat_id)"),
        ("ix_chats_created_by", "chats (created_by)"),
        ("ix_chat_audit_log_actor", "chat_audit_log (actor_id)"),
        ("ix_chat_audit_log_target", "chat_audit_log (target_user_id) WHERE target_user_id IS NOT NULL"),
    ];

    public override void Up()
    {
        foreach (var (name, definition) in Indexes)
            Execute.Sql($"CREATE INDEX CONCURRENTLY IF NOT EXISTS {name} ON {definition}");
    }

    public override void Down()
    {
        foreach (var (name, _) in Indexes)
            Execute.Sql($"DROP INDEX CONCURRENTLY IF EXISTS {name}");
    }
}
