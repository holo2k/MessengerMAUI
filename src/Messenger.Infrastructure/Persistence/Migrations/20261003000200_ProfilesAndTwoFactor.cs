using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261003000200_ProfilesAndTwoFactor")]
public sealed class ProfilesAndTwoFactor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("user_profiles", table => new
        {
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
            normalized_username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
            bio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            avatar_object_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_user_profiles", x => x.user_id);
            table.ForeignKey("FK_user_profiles_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("user_privacy_settings", table => new
        {
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            phone_visibility = table.Column<int>(type: "integer", nullable: false),
            avatar_visibility = table.Column<int>(type: "integer", nullable: false),
            last_seen_visibility = table.Column<int>(type: "integer", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_user_privacy_settings", x => x.user_id);
            table.ForeignKey("FK_user_privacy_settings_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("user_security_settings", table => new
        {
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
            email_ciphertext = table.Column<string>(type: "text", nullable: true),
            email_nonce = table.Column<string>(type: "text", nullable: true),
            email_tag = table.Column<string>(type: "text", nullable: true),
            email_key_version = table.Column<int>(type: "integer", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_user_security_settings", x => x.user_id);
            table.ForeignKey("FK_user_security_settings_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("email_code_challenges", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            purpose = table.Column<int>(type: "integer", nullable: false),
            email_ciphertext = table.Column<string>(type: "text", nullable: false),
            email_nonce = table.Column<string>(type: "text", nullable: false),
            email_tag = table.Column<string>(type: "text", nullable: false),
            email_key_version = table.Column<int>(type: "integer", nullable: false),
            code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            attempts = table.Column<int>(type: "integer", nullable: false),
            maximum_attempts = table.Column<int>(type: "integer", nullable: false),
            consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_email_code_challenges", x => x.Id);
            table.ForeignKey("FK_email_code_challenges_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("pending_logins", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            email_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
            device_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_pending_logins", x => x.Id);
            table.ForeignKey("FK_pending_logins_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_pending_logins_email_code_challenges_email_challenge_id", x => x.email_challenge_id, "email_code_challenges", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateIndex("IX_user_profiles_normalized_username", "user_profiles", "normalized_username", unique: true, filter: "normalized_username IS NOT NULL");
        migrationBuilder.CreateIndex("IX_email_code_challenges_user_id", "email_code_challenges", "user_id");
        migrationBuilder.CreateIndex("IX_pending_logins_email_challenge_id", "pending_logins", "email_challenge_id", unique: true);
        migrationBuilder.CreateIndex("IX_pending_logins_token_hash", "pending_logins", "token_hash", unique: true);
        migrationBuilder.CreateIndex("IX_pending_logins_user_id", "pending_logins", "user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("pending_logins");
        migrationBuilder.DropTable("user_privacy_settings");
        migrationBuilder.DropTable("user_profiles");
        migrationBuilder.DropTable("user_security_settings");
        migrationBuilder.DropTable("email_code_challenges");
    }
}
