using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261003000300_ContactsChatsAndFolders")]
public sealed class ContactsChatsAndFolders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("chats", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            type = table.Column<int>(type: "integer", nullable: false),
            created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
            title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
            avatar_object_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            current_message_sequence = table.Column<long>(type: "bigint", nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_chats", x => x.Id);
            table.ForeignKey("FK_chats_users_created_by_user_id", x => x.created_by_user_id, "users", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable("contacts", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
            target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
            local_first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
            local_last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
            is_muted = table.Column<bool>(type: "boolean", nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_contacts", x => x.Id);
            table.ForeignKey("FK_contacts_users_owner_user_id", x => x.owner_user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_contacts_users_target_user_id", x => x.target_user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("chat_folders", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            position = table.Column<int>(type: "integer", nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_chat_folders", x => x.Id);
            table.ForeignKey("FK_chat_folders_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("direct_chat_pairs", table => new
        {
            chat_id = table.Column<Guid>(type: "uuid", nullable: false),
            lower_user_id = table.Column<Guid>(type: "uuid", nullable: false),
            higher_user_id = table.Column<Guid>(type: "uuid", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_direct_chat_pairs", x => x.chat_id);
            table.ForeignKey("FK_direct_chat_pairs_chats_chat_id", x => x.chat_id, "chats", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_direct_chat_pairs_users_lower_user_id", x => x.lower_user_id, "users", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_direct_chat_pairs_users_higher_user_id", x => x.higher_user_id, "users", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable("chat_members", table => new
        {
            chat_id = table.Column<Guid>(type: "uuid", nullable: false),
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            role = table.Column<int>(type: "integer", nullable: false),
            joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            is_muted = table.Column<bool>(type: "boolean", nullable: false),
            is_archived = table.Column<bool>(type: "boolean", nullable: false),
            hidden_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            last_read_sequence = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_chat_members", x => new { x.chat_id, x.user_id });
            table.ForeignKey("FK_chat_members_chats_chat_id", x => x.chat_id, "chats", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_chat_members_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("chat_folder_items", table => new
        {
            folder_id = table.Column<Guid>(type: "uuid", nullable: false),
            chat_id = table.Column<Guid>(type: "uuid", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_chat_folder_items", x => new { x.folder_id, x.chat_id });
            table.ForeignKey("FK_chat_folder_items_chat_folders_folder_id", x => x.folder_id, "chat_folders", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_chat_folder_items_chats_chat_id", x => x.chat_id, "chats", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateIndex("IX_chats_created_by_user_id", "chats", "created_by_user_id");
        migrationBuilder.CreateIndex("IX_contacts_owner_user_id_target_user_id", "contacts", new[] { "owner_user_id", "target_user_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_contacts_target_user_id", "contacts", "target_user_id");
        migrationBuilder.CreateIndex("IX_chat_folders_user_id_position", "chat_folders", new[] { "user_id", "position" });
        migrationBuilder.CreateIndex("IX_direct_chat_pairs_lower_user_id_higher_user_id", "direct_chat_pairs", new[] { "lower_user_id", "higher_user_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_direct_chat_pairs_higher_user_id", "direct_chat_pairs", "higher_user_id");
        migrationBuilder.CreateIndex("IX_chat_members_user_id", "chat_members", "user_id");
        migrationBuilder.CreateIndex("IX_chat_folder_items_chat_id", "chat_folder_items", "chat_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("chat_folder_items");
        migrationBuilder.DropTable("chat_members");
        migrationBuilder.DropTable("direct_chat_pairs");
        migrationBuilder.DropTable("chat_folders");
        migrationBuilder.DropTable("chats");
        migrationBuilder.DropTable("contacts");
    }
}
