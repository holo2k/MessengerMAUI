using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Messenger.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MessengerDbContext))]
[Migration("20261003000600_Music")]
public sealed class Music : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("music_tracks", columns: t => new
        {
            Id = t.Column<Guid>("uuid", nullable: false), uploader_user_id = t.Column<Guid>("uuid", nullable: false),
            audio_object_id = t.Column<Guid>("uuid", nullable: false), cover_object_id = t.Column<Guid>("uuid", nullable: true),
            title = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
            artist = t.Column<string>("character varying(200)", maxLength: 200, nullable: false),
            duration_ms = t.Column<long>("bigint", nullable: false), status = t.Column<int>("integer", nullable: false),
            created_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false),
            updated_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_music_tracks", x => x.Id);
            t.ForeignKey("FK_music_tracks_users_uploader_user_id", x => x.uploader_user_id, "users", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_music_tracks_stored_objects_audio_object_id", x => x.audio_object_id, "stored_objects", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_music_tracks_stored_objects_cover_object_id", x => x.cover_object_id, "stored_objects", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateTable("rights_declarations", columns: t => new
        {
            Id = t.Column<Guid>("uuid", nullable: false), track_id = t.Column<Guid>("uuid", nullable: false),
            user_id = t.Column<Guid>("uuid", nullable: false), statement = t.Column<string>("text", nullable: false),
            created_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false)
        }, constraints: t => { t.PrimaryKey("PK_rights_declarations", x => x.Id); t.ForeignKey("FK_rights_declarations_music_tracks_track_id", x => x.track_id, "music_tracks", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateTable("user_music_tracks", columns: t => new
        {
            user_id = t.Column<Guid>("uuid", nullable: false), track_id = t.Column<Guid>("uuid", nullable: false),
            added_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false)
        }, constraints: t => { t.PrimaryKey("PK_user_music_tracks", x => new { x.user_id, x.track_id }); t.ForeignKey("FK_user_music_tracks_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade); t.ForeignKey("FK_user_music_tracks_music_tracks_track_id", x => x.track_id, "music_tracks", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateTable("copyright_claims", columns: t => new
        {
            Id = t.Column<Guid>("uuid", nullable: false), track_id = t.Column<Guid>("uuid", nullable: false),
            claimant_user_id = t.Column<Guid>("uuid", nullable: false), details = t.Column<string>("text", nullable: false),
            status = t.Column<int>("integer", nullable: false), created_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false)
        }, constraints: t => { t.PrimaryKey("PK_copyright_claims", x => x.Id); t.ForeignKey("FK_copyright_claims_music_tracks_track_id", x => x.track_id, "music_tracks", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateTable("moderation_actions", columns: t => new
        {
            Id = t.Column<Guid>("uuid", nullable: false), track_id = t.Column<Guid>("uuid", nullable: false),
            administrator_user_id = t.Column<Guid>("uuid", nullable: false), action = t.Column<string>("character varying(30)", maxLength: 30, nullable: false),
            reason = t.Column<string>("text", nullable: false), created_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false)
        }, constraints: t => { t.PrimaryKey("PK_moderation_actions", x => x.Id); t.ForeignKey("FK_moderation_actions_music_tracks_track_id", x => x.track_id, "music_tracks", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateIndex("IX_music_tracks_uploader_user_id", "music_tracks", "uploader_user_id");
        m.CreateIndex("IX_music_tracks_audio_object_id", "music_tracks", "audio_object_id");
        m.CreateIndex("IX_music_tracks_cover_object_id", "music_tracks", "cover_object_id");
        m.CreateIndex("IX_rights_declarations_track_id", "rights_declarations", "track_id", unique: true);
        m.CreateIndex("IX_user_music_tracks_track_id", "user_music_tracks", "track_id");
        m.CreateIndex("IX_copyright_claims_track_id", "copyright_claims", "track_id");
        m.CreateIndex("IX_moderation_actions_track_id", "moderation_actions", "track_id");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("copyright_claims"); m.DropTable("moderation_actions");
        m.DropTable("rights_declarations"); m.DropTable("user_music_tracks"); m.DropTable("music_tracks");
    }
}
