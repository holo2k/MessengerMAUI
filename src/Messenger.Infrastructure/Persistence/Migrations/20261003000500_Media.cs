using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261003000500_Media")]
public sealed class Media : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("stored_objects", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
            object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
            file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
            content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            size = table.Column<long>(type: "bigint", nullable: false),
            sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            status = table.Column<int>(type: "integer", nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_stored_objects", x => x.Id);
            table.ForeignKey("FK_stored_objects_users_owner_user_id", x => x.owner_user_id, "users", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable("upload_sessions", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
            object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
            file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
            content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            declared_size = table.Column<long>(type: "bigint", nullable: false),
            sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            status = table.Column<int>(type: "integer", nullable: false),
            stored_object_id = table.Column<Guid>(type: "uuid", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_upload_sessions", x => x.Id);
            table.ForeignKey("FK_upload_sessions_users_owner_user_id", x => x.owner_user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("message_attachments", table => new
        {
            message_id = table.Column<Guid>(type: "uuid", nullable: false),
            object_id = table.Column<Guid>(type: "uuid", nullable: false),
            position = table.Column<int>(type: "integer", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_message_attachments", x => new { x.message_id, x.object_id });
            table.ForeignKey("FK_message_attachments_messages_message_id", x => x.message_id, "messages", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_message_attachments_stored_objects_object_id", x => x.object_id, "stored_objects", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateIndex("IX_stored_objects_object_key", "stored_objects", "object_key", unique: true);
        migrationBuilder.CreateIndex("IX_stored_objects_owner_user_id", "stored_objects", "owner_user_id");
        migrationBuilder.CreateIndex("IX_upload_sessions_object_key", "upload_sessions", "object_key", unique: true);
        migrationBuilder.CreateIndex("IX_upload_sessions_owner_user_id", "upload_sessions", "owner_user_id");
        migrationBuilder.CreateIndex("IX_upload_sessions_status_expires_at", "upload_sessions", new[] { "status", "expires_at" });
        migrationBuilder.CreateIndex("IX_message_attachments_object_id", "message_attachments", "object_id");
        migrationBuilder.CreateIndex("IX_message_attachments_message_id_position", "message_attachments", new[] { "message_id", "position" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("message_attachments");
        migrationBuilder.DropTable("upload_sessions");
        migrationBuilder.DropTable("stored_objects");
    }
}
