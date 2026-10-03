using Messenger.Domain.Identity;
using Messenger.Domain.Media;
using Messenger.Domain.Music;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class MusicTrackConfiguration : IEntityTypeConfiguration<MusicTrack>
{
    public void Configure(EntityTypeBuilder<MusicTrack> b)
    {
        b.ToTable("music_tracks"); b.HasKey(x => x.Id);
        b.Property(x => x.UploaderUserId).HasColumnName("uploader_user_id"); b.Property(x => x.AudioObjectId).HasColumnName("audio_object_id");
        b.Property(x => x.CoverObjectId).HasColumnName("cover_object_id"); b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
        b.Property(x => x.Artist).HasColumnName("artist").HasMaxLength(200); b.Property(x => x.DurationMs).HasColumnName("duration_ms");
        b.Property(x => x.Status).HasColumnName("status"); b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UploaderUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredObject>().WithMany().HasForeignKey(x => x.AudioObjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredObject>().WithMany().HasForeignKey(x => x.CoverObjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Declaration).WithOne().HasForeignKey<RightsDeclaration>(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class RightsDeclarationConfiguration : IEntityTypeConfiguration<RightsDeclaration>
{
    public void Configure(EntityTypeBuilder<RightsDeclaration> b) { b.ToTable("rights_declarations"); b.HasKey(x => x.Id); b.Property(x => x.TrackId).HasColumnName("track_id"); b.Property(x => x.UserId).HasColumnName("user_id"); b.Property(x => x.Statement).HasColumnName("statement"); b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.HasIndex(x => x.TrackId).IsUnique(); }
}
public sealed class UserMusicTrackConfiguration : IEntityTypeConfiguration<UserMusicTrack>
{
    public void Configure(EntityTypeBuilder<UserMusicTrack> b) { b.ToTable("user_music_tracks"); b.HasKey(x => new { x.UserId, x.TrackId }); b.Property(x => x.UserId).HasColumnName("user_id"); b.Property(x => x.TrackId).HasColumnName("track_id"); b.Property(x => x.AddedAt).HasColumnName("added_at"); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); b.HasOne<MusicTrack>().WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade); }
}
public sealed class CopyrightClaimConfiguration : IEntityTypeConfiguration<CopyrightClaim>
{
    public void Configure(EntityTypeBuilder<CopyrightClaim> b) { b.ToTable("copyright_claims"); b.HasKey(x => x.Id); b.Property(x => x.TrackId).HasColumnName("track_id"); b.Property(x => x.ClaimantUserId).HasColumnName("claimant_user_id"); b.Property(x => x.Details).HasColumnName("details"); b.Property(x => x.Status).HasColumnName("status"); b.Property(x => x.CreatedAt).HasColumnName("created_at"); }
}
public sealed class ModerationActionConfiguration : IEntityTypeConfiguration<ModerationAction>
{
    public void Configure(EntityTypeBuilder<ModerationAction> b) { b.ToTable("moderation_actions"); b.HasKey(x => x.Id); b.Property(x => x.TrackId).HasColumnName("track_id"); b.Property(x => x.AdministratorUserId).HasColumnName("administrator_user_id"); b.Property(x => x.Action).HasColumnName("action").HasMaxLength(30); b.Property(x => x.Reason).HasColumnName("reason"); b.Property(x => x.CreatedAt).HasColumnName("created_at"); }
}
