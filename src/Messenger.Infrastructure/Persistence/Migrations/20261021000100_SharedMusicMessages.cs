using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261021000100_SharedMusicMessages")]
public sealed class SharedMusicMessages : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<Guid>(name: "music_track_id", table: "messages", type: "uuid", nullable: true);
        m.CreateIndex(name: "IX_messages_music_track_id", table: "messages", column: "music_track_id");
        m.AddForeignKey(name: "FK_messages_music_tracks_music_track_id", table: "messages", column: "music_track_id",
            principalTable: "music_tracks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropForeignKey(name: "FK_messages_music_tracks_music_track_id", table: "messages");
        m.DropIndex(name: "IX_messages_music_track_id", table: "messages");
        m.DropColumn(name: "music_track_id", table: "messages");
    }
}
