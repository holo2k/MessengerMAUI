using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261023000100_VoiceAndVideoNotes")]
public sealed class VoiceAndVideoNotes : Migration
{
    protected override void Up(MigrationBuilder migration)
    {
        migration.AddColumn<long>(name: "duration_ms", table: "messages", type: "bigint", nullable: true);
        migration.AddColumn<byte[]>(name: "waveform", table: "messages", type: "bytea", nullable: true);
    }
    protected override void Down(MigrationBuilder migration)
    {
        migration.DropColumn(name: "duration_ms", table: "messages");
        migration.DropColumn(name: "waveform", table: "messages");
    }
}
