using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261012000100_PublishPendingMusic")]
public sealed class PublishPendingMusic : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql("""
            INSERT INTO user_music_tracks (user_id, track_id, added_at)
            SELECT uploader_user_id, "Id", created_at FROM music_tracks WHERE status = 0
            ON CONFLICT (user_id, track_id) DO NOTHING;

            UPDATE music_tracks SET status = 1, updated_at = CURRENT_TIMESTAMP WHERE status = 0;
            """);
    }

    protected override void Down(MigrationBuilder m)
    {
        // Keep published tracks and library entries when rolling back this data migration.
    }
}
