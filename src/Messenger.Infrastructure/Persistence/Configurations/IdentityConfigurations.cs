using Messenger.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.PhoneCiphertext).HasColumnName("phone_ciphertext").IsRequired();
        builder.Property(user => user.PhoneNonce).HasColumnName("phone_nonce").IsRequired();
        builder.Property(user => user.PhoneTag).HasColumnName("phone_tag").IsRequired();
        builder.Property(user => user.PhoneKeyVersion).HasColumnName("phone_key_version");
        builder.Property(user => user.PhoneBlindIndex).HasColumnName("phone_blind_index").HasMaxLength(128).IsRequired();
        builder.Property(user => user.CreatedAt).HasColumnName("created_at");
        builder.Property(user => user.DeactivatedAt).HasColumnName("deactivated_at");
        builder.HasIndex(user => user.PhoneBlindIndex).IsUnique();
    }
}

public sealed class LoginChallengeConfiguration : IEntityTypeConfiguration<LoginChallenge>
{
    public void Configure(EntityTypeBuilder<LoginChallenge> builder)
    {
        builder.ToTable("login_challenges");
        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.Purpose).HasColumnName("purpose");
        builder.Property(challenge => challenge.PhoneCiphertext).HasColumnName("phone_ciphertext").IsRequired();
        builder.Property(challenge => challenge.PhoneNonce).HasColumnName("phone_nonce").IsRequired();
        builder.Property(challenge => challenge.PhoneTag).HasColumnName("phone_tag").IsRequired();
        builder.Property(challenge => challenge.PhoneKeyVersion).HasColumnName("phone_key_version");
        builder.Property(challenge => challenge.PhoneBlindIndex).HasColumnName("phone_blind_index").HasMaxLength(128).IsRequired();
        builder.Property(challenge => challenge.CodeHash).HasColumnName("code_hash").HasMaxLength(128).IsRequired();
        builder.Property(challenge => challenge.ExpiresAt).HasColumnName("expires_at");
        builder.Property(challenge => challenge.Attempts).HasColumnName("attempts");
        builder.Property(challenge => challenge.MaximumAttempts).HasColumnName("maximum_attempts");
        builder.Property(challenge => challenge.ConsumedAt).HasColumnName("consumed_at");
        builder.HasIndex(challenge => challenge.PhoneBlindIndex);
    }
}

public sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.ToTable("refresh_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.UserId).HasColumnName("user_id");
        builder.Property(session => session.DeviceLabel).HasColumnName("device_label").HasMaxLength(200).IsRequired();
        builder.Property(session => session.TokenFamilyId).HasColumnName("token_family_id");
        builder.Property(session => session.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.Property(session => session.CreatedAt).HasColumnName("created_at");
        builder.Property(session => session.ExpiresAt).HasColumnName("expires_at");
        builder.Property(session => session.ReplacedBySessionId).HasColumnName("replaced_by_session_id");
        builder.Property(session => session.RevokedAt).HasColumnName("revoked_at");
        builder.Property(session => session.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(100);
        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => session.TokenFamilyId);
        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
