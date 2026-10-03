using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261003000700_SupportAndDeletion")]
public sealed class SupportAndDeletion : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("support_tickets", columns: t => new
        {
            Id = t.Column<Guid>("uuid", nullable: false), owner_user_id = t.Column<Guid>("uuid", nullable: false),
            subject = t.Column<string>("character varying(200)", maxLength: 200, nullable: false), status = t.Column<int>("integer", nullable: false),
            created_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false), closed_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: true)
        }, constraints: t => { t.PrimaryKey("PK_support_tickets", x => x.Id); t.ForeignKey("FK_support_tickets_users_owner_user_id", x => x.owner_user_id, "users", "Id", onDelete: ReferentialAction.Restrict); });
        m.CreateTable("account_deletion_requests", columns: t => new
        {
            Id = t.Column<Guid>("uuid", nullable: false), user_id = t.Column<Guid>("uuid", nullable: false), requested_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false),
            execute_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false), cancelled_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: true), executed_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: true)
        }, constraints: t => { t.PrimaryKey("PK_account_deletion_requests", x => x.Id); t.ForeignKey("FK_account_deletion_requests_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Restrict); });
        m.CreateTable("support_messages", columns: t => new
        {
            Id = t.Column<Guid>("uuid", nullable: false), ticket_id = t.Column<Guid>("uuid", nullable: false), author_user_id = t.Column<Guid>("uuid", nullable: false),
            body = t.Column<string>("character varying(4000)", maxLength: 4000, nullable: false), is_staff = t.Column<bool>("boolean", nullable: false), created_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false)
        }, constraints: t => { t.PrimaryKey("PK_support_messages", x => x.Id); t.ForeignKey("FK_support_messages_support_tickets_ticket_id", x => x.ticket_id, "support_tickets", "Id", onDelete: ReferentialAction.Cascade); t.ForeignKey("FK_support_messages_users_author_user_id", x => x.author_user_id, "users", "Id", onDelete: ReferentialAction.Restrict); });
        m.CreateIndex("IX_support_tickets_owner_user_id", "support_tickets", "owner_user_id");
        m.CreateIndex("IX_support_messages_ticket_id", "support_messages", "ticket_id"); m.CreateIndex("IX_support_messages_author_user_id", "support_messages", "author_user_id");
        m.CreateIndex("IX_account_deletion_requests_user_id", "account_deletion_requests", "user_id");
    }
    protected override void Down(MigrationBuilder m) { m.DropTable("support_messages"); m.DropTable("account_deletion_requests"); m.DropTable("support_tickets"); }
}
