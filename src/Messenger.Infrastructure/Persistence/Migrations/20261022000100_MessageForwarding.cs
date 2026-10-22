using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261022000100_MessageForwarding")]
public sealed class MessageForwarding : Migration
{
    protected override void Up(MigrationBuilder migration) =>
        migration.AddColumn<bool>(name: "is_forwarded", table: "messages", type: "boolean", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migration) => migration.DropColumn(name: "is_forwarded", table: "messages");
}
