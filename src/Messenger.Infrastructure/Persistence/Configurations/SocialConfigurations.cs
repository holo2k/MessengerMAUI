using Messenger.Domain.Chats;
using Messenger.Domain.Contacts;
using Messenger.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(contact => contact.Id);
        builder.Property(contact => contact.OwnerUserId).HasColumnName("owner_user_id");
        builder.Property(contact => contact.TargetUserId).HasColumnName("target_user_id");
        builder.Property(contact => contact.LocalFirstName).HasColumnName("local_first_name").HasMaxLength(100);
        builder.Property(contact => contact.LocalLastName).HasColumnName("local_last_name").HasMaxLength(100);
        builder.Property(contact => contact.IsMuted).HasColumnName("is_muted");
        builder.Property(contact => contact.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(contact => new { contact.OwnerUserId, contact.TargetUserId }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(contact => contact.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(contact => contact.TargetUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatConfiguration : IEntityTypeConfiguration<Chat>
{
    public void Configure(EntityTypeBuilder<Chat> builder)
    {
        builder.ToTable("chats");
        builder.HasKey(chat => chat.Id);
        builder.Property(chat => chat.Type).HasColumnName("type");
        builder.Property(chat => chat.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(chat => chat.Title).HasColumnName("title").HasMaxLength(200);
        builder.Property(chat => chat.AvatarObjectId).HasColumnName("avatar_object_id").HasMaxLength(500);
        builder.Property(chat => chat.CurrentMessageSequence).HasColumnName("current_message_sequence");
        builder.Property(chat => chat.CreatedAt).HasColumnName("created_at");
        builder.Property(chat => chat.UpdatedAt).HasColumnName("updated_at");
        builder.Property(chat => chat.DeletedAt).HasColumnName("deleted_at");
        builder.HasOne<User>().WithMany().HasForeignKey(chat => chat.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DirectChatPairConfiguration : IEntityTypeConfiguration<DirectChatPair>
{
    public void Configure(EntityTypeBuilder<DirectChatPair> builder)
    {
        builder.ToTable("direct_chat_pairs");
        builder.HasKey(pair => pair.ChatId);
        builder.Property(pair => pair.ChatId).HasColumnName("chat_id");
        builder.Property(pair => pair.LowerUserId).HasColumnName("lower_user_id");
        builder.Property(pair => pair.HigherUserId).HasColumnName("higher_user_id");
        builder.HasIndex(pair => new { pair.LowerUserId, pair.HigherUserId }).IsUnique();
        builder.HasOne<Chat>().WithOne().HasForeignKey<DirectChatPair>(pair => pair.ChatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(pair => pair.LowerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(pair => pair.HigherUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ChatMemberConfiguration : IEntityTypeConfiguration<ChatMember>
{
    public void Configure(EntityTypeBuilder<ChatMember> builder)
    {
        builder.ToTable("chat_members");
        builder.HasKey(member => new { member.ChatId, member.UserId });
        builder.Property(member => member.ChatId).HasColumnName("chat_id");
        builder.Property(member => member.UserId).HasColumnName("user_id");
        builder.Property(member => member.Role).HasColumnName("role");
        builder.Property(member => member.JoinedAt).HasColumnName("joined_at");
        builder.Property(member => member.LeftAt).HasColumnName("left_at");
        builder.Property(member => member.IsMuted).HasColumnName("is_muted");
        builder.Property(member => member.IsArchived).HasColumnName("is_archived");
        builder.Property(member => member.HiddenAt).HasColumnName("hidden_at");
        builder.Property(member => member.LastReadSequence).HasColumnName("last_read_sequence");
        builder.Ignore(member => member.IsActive);
        builder.HasOne<Chat>().WithMany().HasForeignKey(member => member.ChatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(member => member.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatFolderConfiguration : IEntityTypeConfiguration<ChatFolder>
{
    public void Configure(EntityTypeBuilder<ChatFolder> builder)
    {
        builder.ToTable("chat_folders");
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.UserId).HasColumnName("user_id");
        builder.Property(folder => folder.Title).HasColumnName("title").HasMaxLength(100);
        builder.Property(folder => folder.Position).HasColumnName("position");
        builder.Property(folder => folder.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(folder => new { folder.UserId, folder.Position });
        builder.HasOne<User>().WithMany().HasForeignKey(folder => folder.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatFolderItemConfiguration : IEntityTypeConfiguration<ChatFolderItem>
{
    public void Configure(EntityTypeBuilder<ChatFolderItem> builder)
    {
        builder.ToTable("chat_folder_items");
        builder.HasKey(item => new { item.FolderId, item.ChatId });
        builder.Property(item => item.FolderId).HasColumnName("folder_id");
        builder.Property(item => item.ChatId).HasColumnName("chat_id");
        builder.HasOne<ChatFolder>().WithMany().HasForeignKey(item => item.FolderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Chat>().WithMany().HasForeignKey(item => item.ChatId).OnDelete(DeleteBehavior.Cascade);
    }
}
