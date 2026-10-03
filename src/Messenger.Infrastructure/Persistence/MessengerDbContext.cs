using Microsoft.EntityFrameworkCore;
using Messenger.Domain.Identity;
using Messenger.Domain.Users;
using Messenger.Domain.Contacts;
using Messenger.Domain.Chats;

namespace Messenger.Infrastructure.Persistence;

public sealed class MessengerDbContext(DbContextOptions<MessengerDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<LoginChallenge> LoginChallenges => Set<LoginChallenge>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserPrivacySettings> UserPrivacySettings => Set<UserPrivacySettings>();
    public DbSet<UserSecuritySettings> UserSecuritySettings => Set<UserSecuritySettings>();
    public DbSet<EmailCodeChallenge> EmailCodeChallenges => Set<EmailCodeChallenge>();
    public DbSet<PendingLogin> PendingLogins => Set<PendingLogin>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<DirectChatPair> DirectChatPairs => Set<DirectChatPair>();
    public DbSet<ChatMember> ChatMembers => Set<ChatMember>();
    public DbSet<ChatFolder> ChatFolders => Set<ChatFolder>();
    public DbSet<ChatFolderItem> ChatFolderItems => Set<ChatFolderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MessengerDbContext).Assembly);
}
