using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261003000100_Identity")]
public sealed class Identity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "login_challenges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                purpose = table.Column<int>(type: "integer", nullable: false),
                phone_ciphertext = table.Column<string>(type: "text", nullable: false),
                phone_nonce = table.Column<string>(type: "text", nullable: false),
                phone_tag = table.Column<string>(type: "text", nullable: false),
                phone_key_version = table.Column<int>(type: "integer", nullable: false),
                phone_blind_index = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                maximum_attempts = table.Column<int>(type: "integer", nullable: false),
                consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_login_challenges", x => x.Id));

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                phone_ciphertext = table.Column<string>(type: "text", nullable: false),
                phone_nonce = table.Column<string>(type: "text", nullable: false),
                phone_tag = table.Column<string>(type: "text", nullable: false),
                phone_key_version = table.Column<int>(type: "integer", nullable: false),
                phone_blind_index = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_users", x => x.Id));

        migrationBuilder.CreateTable(
            name: "refresh_sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                device_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                token_family_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                replaced_by_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revocation_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refresh_sessions", x => x.Id);
                table.ForeignKey("FK_refresh_sessions_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_login_challenges_phone_blind_index", "login_challenges", "phone_blind_index");
        migrationBuilder.CreateIndex("IX_users_phone_blind_index", "users", "phone_blind_index", unique: true);
        migrationBuilder.CreateIndex("IX_refresh_sessions_token_family_id", "refresh_sessions", "token_family_id");
        migrationBuilder.CreateIndex("IX_refresh_sessions_token_hash", "refresh_sessions", "token_hash", unique: true);
        migrationBuilder.CreateIndex("IX_refresh_sessions_user_id", "refresh_sessions", "user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("login_challenges");
        migrationBuilder.DropTable("refresh_sessions");
        migrationBuilder.DropTable("users");
    }
}
