using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261003000400_Messages")]
public sealed class Messages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("messages", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            chat_id = table.Column<Guid>(type: "uuid", nullable: false),
            sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
            sequence = table.Column<long>(type: "bigint", nullable: false),
            client_message_id = table.Column<Guid>(type: "uuid", nullable: false),
            kind = table.Column<int>(type: "integer", nullable: false),
            body_ciphertext = table.Column<string>(type: "text", nullable: false),
            body_nonce = table.Column<string>(type: "text", nullable: false),
            body_tag = table.Column<string>(type: "text", nullable: false),
            body_key_version = table.Column<int>(type: "integer", nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_messages", x => x.Id);
            table.ForeignKey("FK_messages_chats_chat_id", x => x.chat_id, "chats", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_messages_users_sender_user_id", x => x.sender_user_id, "users", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable("hidden_messages", table => new
        {
            message_id = table.Column<Guid>(type: "uuid", nullable: false),
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            hidden_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_hidden_messages", x => new { x.message_id, x.user_id });
            table.ForeignKey("FK_hidden_messages_messages_message_id", x => x.message_id, "messages", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_hidden_messages_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("message_search_tokens", table => new
        {
            message_id = table.Column<Guid>(type: "uuid", nullable: false),
            token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_message_search_tokens", x => new { x.message_id, x.token });
            table.ForeignKey("FK_message_search_tokens_messages_message_id", x => x.message_id, "messages", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("pinned_messages", table => new
        {
            chat_id = table.Column<Guid>(type: "uuid", nullable: false),
            message_id = table.Column<Guid>(type: "uuid", nullable: false),
            pinned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
            pinned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_pinned_messages", x => new { x.chat_id, x.message_id });
            table.ForeignKey("FK_pinned_messages_chats_chat_id", x => x.chat_id, "chats", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_pinned_messages_messages_message_id", x => x.message_id, "messages", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_pinned_messages_users_pinned_by_user_id", x => x.pinned_by_user_id, "users", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateIndex("IX_messages_chat_id_sequence", "messages", new[] { "chat_id", "sequence" }, unique: true);
        migrationBuilder.CreateIndex("IX_messages_sender_user_id_client_message_id", "messages", new[] { "sender_user_id", "client_message_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_hidden_messages_user_id", "hidden_messages", "user_id");
        migrationBuilder.CreateIndex("IX_message_search_tokens_token", "message_search_tokens", "token");
        migrationBuilder.CreateIndex("IX_pinned_messages_message_id", "pinned_messages", "message_id");
        migrationBuilder.CreateIndex("IX_pinned_messages_pinned_by_user_id", "pinned_messages", "pinned_by_user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("hidden_messages");
        migrationBuilder.DropTable("message_search_tokens");
        migrationBuilder.DropTable("pinned_messages");
        migrationBuilder.DropTable("messages");
    }
}
