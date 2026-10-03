using Messenger.Domain.Chats;
using Messenger.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.ChatId).HasColumnName("chat_id");
        builder.Property(message => message.SenderUserId).HasColumnName("sender_user_id");
        builder.Property(message => message.Sequence).HasColumnName("sequence");
        builder.Property(message => message.ClientMessageId).HasColumnName("client_message_id");
        builder.Property(message => message.Kind).HasColumnName("kind");
        builder.Property(message => message.BodyCiphertext).HasColumnName("body_ciphertext");
        builder.Property(message => message.BodyNonce).HasColumnName("body_nonce");
        builder.Property(message => message.BodyTag).HasColumnName("body_tag");
        builder.Property(message => message.BodyKeyVersion).HasColumnName("body_key_version");
        builder.Property(message => message.CreatedAt).HasColumnName("created_at");
        builder.Property(message => message.EditedAt).HasColumnName("edited_at");
        builder.Property(message => message.DeletedAt).HasColumnName("deleted_at");
        builder.Property(message => message.DeletedByUserId).HasColumnName("deleted_by_user_id");
        builder.HasIndex(message => new { message.SenderUserId, message.ClientMessageId }).IsUnique();
        builder.HasIndex(message => new { message.ChatId, message.Sequence }).IsUnique();
        builder.HasOne<Chat>().WithMany().HasForeignKey(message => message.ChatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(message => message.SenderUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MessageSearchTokenConfiguration : IEntityTypeConfiguration<MessageSearchToken>
{
    public void Configure(EntityTypeBuilder<MessageSearchToken> builder)
    {
        builder.ToTable("message_search_tokens");
        builder.HasKey(token => new { token.MessageId, token.Token });
        builder.Property(token => token.MessageId).HasColumnName("message_id");
        builder.Property(token => token.Token).HasColumnName("token").HasMaxLength(128);
        builder.HasIndex(token => token.Token);
        builder.HasOne<Message>().WithMany().HasForeignKey(token => token.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class HiddenMessageConfiguration : IEntityTypeConfiguration<HiddenMessage>
{
    public void Configure(EntityTypeBuilder<HiddenMessage> builder)
    {
        builder.ToTable("hidden_messages");
        builder.HasKey(hidden => new { hidden.MessageId, hidden.UserId });
        builder.Property(hidden => hidden.MessageId).HasColumnName("message_id");
        builder.Property(hidden => hidden.UserId).HasColumnName("user_id");
        builder.Property(hidden => hidden.HiddenAt).HasColumnName("hidden_at");
        builder.HasOne<Message>().WithMany().HasForeignKey(hidden => hidden.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(hidden => hidden.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PinnedMessageConfiguration : IEntityTypeConfiguration<PinnedMessage>
{
    public void Configure(EntityTypeBuilder<PinnedMessage> builder)
    {
        builder.ToTable("pinned_messages");
        builder.HasKey(pin => new { pin.ChatId, pin.MessageId });
        builder.Property(pin => pin.ChatId).HasColumnName("chat_id");
        builder.Property(pin => pin.MessageId).HasColumnName("message_id");
        builder.Property(pin => pin.PinnedByUserId).HasColumnName("pinned_by_user_id");
        builder.Property(pin => pin.PinnedAt).HasColumnName("pinned_at");
        builder.HasOne<Chat>().WithMany().HasForeignKey(pin => pin.ChatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Message>().WithMany().HasForeignKey(pin => pin.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(pin => pin.PinnedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
