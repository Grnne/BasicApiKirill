using FluentMigrator;

namespace BasicApi.Storage.Migrations;

[Migration(1)]
public class InitialCreate : Migration
{
    public override void Up()
    {
        // Users table
        Create.Table("users")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("username").AsString(100).NotNullable().Unique()
            .WithColumn("email").AsString(200).NotNullable().Unique()
            .WithColumn("password_hash").AsString(int.MaxValue).NotNullable()
            .WithColumn("display_name").AsString(100).NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("last_login_at").AsDateTime().Nullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true);

        // Chats table
        Create.Table("chats")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("title").AsString(200).Nullable()
            .WithColumn("type").AsString(20).NotNullable().WithDefaultValue("private")
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        // ChatMembers table
        Create.Table("chat_members")
            .WithColumn("chat_id").AsGuid().NotNullable()
            .WithColumn("user_id").AsGuid().NotNullable()
            .WithColumn("last_read_message_id").AsGuid().Nullable()
            .WithColumn("joined_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.PrimaryKey("PK_ChatMembers")
            .OnTable("chat_members")
            .Columns("chat_id", "user_id");

        Create.ForeignKey("FK_ChatMembers_Chats")
            .FromTable("chat_members").ForeignColumn("chat_id")
            .ToTable("chats").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("FK_ChatMembers_Users")
            .FromTable("chat_members").ForeignColumn("user_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        // Messages table
        Create.Table("messages")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("chat_id").AsGuid().NotNullable()
            .WithColumn("sender_id").AsGuid().NotNullable()
            .WithColumn("text").AsString(int.MaxValue).NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("is_deleted").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.ForeignKey("FK_Messages_Chats")
            .FromTable("messages").ForeignColumn("chat_id")
            .ToTable("chats").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("FK_Messages_Users")
            .FromTable("messages").ForeignColumn("sender_id")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.None);

        Create.Index("IX_Messages_ChatId_CreatedAt")
            .OnTable("messages")
            .OnColumn("chat_id").Ascending()
            .OnColumn("created_at").Descending();
    }

    public override void Down()
    {
        Delete.Index("IX_Messages_ChatId_CreatedAt").OnTable("messages");
        Delete.ForeignKey("FK_Messages_Chats").OnTable("messages");
        Delete.ForeignKey("FK_Messages_Users").OnTable("messages");
        Delete.Table("messages");
        Delete.ForeignKey("FK_ChatMembers_Chats").OnTable("chat_members");
        Delete.ForeignKey("FK_ChatMembers_Users").OnTable("chat_members");
        Delete.Table("chat_members");
        Delete.Table("chats");
        Delete.Table("users");
    }
}