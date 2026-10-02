using Messenger.Domain.Identity;
using Messenger.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");
        builder.Ignore(profile => profile.Id);
        builder.HasKey(profile => profile.UserId);
        builder.Property(profile => profile.UserId).HasColumnName("user_id");
        builder.Property(profile => profile.FirstName).HasColumnName("first_name").HasMaxLength(100);
        builder.Property(profile => profile.LastName).HasColumnName("last_name").HasMaxLength(100);
        builder.Property(profile => profile.Username).HasColumnName("username").HasMaxLength(64);
        builder.Property(profile => profile.NormalizedUsername).HasColumnName("normalized_username").HasMaxLength(64);
        builder.Property(profile => profile.Bio).HasColumnName("bio").HasMaxLength(500);
        builder.Property(profile => profile.AvatarObjectId).HasColumnName("avatar_object_id").HasMaxLength(500);
        builder.Property(profile => profile.LastSeenAt).HasColumnName("last_seen_at");
        builder.HasIndex(profile => profile.NormalizedUsername).IsUnique().HasFilter("normalized_username IS NOT NULL");
        builder.HasOne<User>().WithOne().HasForeignKey<UserProfile>(profile => profile.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserPrivacyConfiguration : IEntityTypeConfiguration<UserPrivacySettings>
{
    public void Configure(EntityTypeBuilder<UserPrivacySettings> builder)
    {
        builder.ToTable("user_privacy_settings");
        builder.Ignore(settings => settings.Id);
        builder.HasKey(settings => settings.UserId);
        builder.Property(settings => settings.UserId).HasColumnName("user_id");
        builder.Property(settings => settings.PhoneVisibility).HasColumnName("phone_visibility");
        builder.Property(settings => settings.AvatarVisibility).HasColumnName("avatar_visibility");
        builder.Property(settings => settings.LastSeenVisibility).HasColumnName("last_seen_visibility");
        builder.HasOne<User>().WithOne().HasForeignKey<UserPrivacySettings>(settings => settings.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserSecurityConfiguration : IEntityTypeConfiguration<UserSecuritySettings>
{
    public void Configure(EntityTypeBuilder<UserSecuritySettings> builder)
    {
        builder.ToTable("user_security_settings");
        builder.Ignore(settings => settings.Id);
        builder.HasKey(settings => settings.UserId);
        builder.Property(settings => settings.UserId).HasColumnName("user_id");
        builder.Property(settings => settings.TwoFactorEnabled).HasColumnName("two_factor_enabled");
        builder.Property(settings => settings.EmailCiphertext).HasColumnName("email_ciphertext");
        builder.Property(settings => settings.EmailNonce).HasColumnName("email_nonce");
        builder.Property(settings => settings.EmailTag).HasColumnName("email_tag");
        builder.Property(settings => settings.EmailKeyVersion).HasColumnName("email_key_version");
        builder.HasOne<User>().WithOne().HasForeignKey<UserSecuritySettings>(settings => settings.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmailCodeChallengeConfiguration : IEntityTypeConfiguration<EmailCodeChallenge>
{
    public void Configure(EntityTypeBuilder<EmailCodeChallenge> builder)
    {
        builder.ToTable("email_code_challenges");
        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.UserId).HasColumnName("user_id");
        builder.Property(challenge => challenge.Purpose).HasColumnName("purpose");
        builder.Property(challenge => challenge.EmailCiphertext).HasColumnName("email_ciphertext");
        builder.Property(challenge => challenge.EmailNonce).HasColumnName("email_nonce");
        builder.Property(challenge => challenge.EmailTag).HasColumnName("email_tag");
        builder.Property(challenge => challenge.EmailKeyVersion).HasColumnName("email_key_version");
        builder.Property(challenge => challenge.CodeHash).HasColumnName("code_hash").HasMaxLength(128);
        builder.Property(challenge => challenge.ExpiresAt).HasColumnName("expires_at");
        builder.Property(challenge => challenge.Attempts).HasColumnName("attempts");
        builder.Property(challenge => challenge.MaximumAttempts).HasColumnName("maximum_attempts");
        builder.Property(challenge => challenge.ConsumedAt).HasColumnName("consumed_at");
        builder.HasOne<User>().WithMany().HasForeignKey(challenge => challenge.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PendingLoginConfiguration : IEntityTypeConfiguration<PendingLogin>
{
    public void Configure(EntityTypeBuilder<PendingLogin> builder)
    {
        builder.ToTable("pending_logins");
        builder.HasKey(login => login.Id);
        builder.Property(login => login.UserId).HasColumnName("user_id");
        builder.Property(login => login.EmailChallengeId).HasColumnName("email_challenge_id");
        builder.Property(login => login.DeviceLabel).HasColumnName("device_label").HasMaxLength(200);
        builder.Property(login => login.TokenHash).HasColumnName("token_hash").HasMaxLength(128);
        builder.Property(login => login.ExpiresAt).HasColumnName("expires_at");
        builder.Property(login => login.ConsumedAt).HasColumnName("consumed_at");
        builder.HasIndex(login => login.TokenHash).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(login => login.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<EmailCodeChallenge>().WithOne().HasForeignKey<PendingLogin>(login => login.EmailChallengeId).OnDelete(DeleteBehavior.Cascade);
    }
}
