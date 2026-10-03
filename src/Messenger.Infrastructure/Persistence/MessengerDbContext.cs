using Microsoft.EntityFrameworkCore;
using Messenger.Domain.Identity;
using Messenger.Domain.Users;
using Messenger.Domain.Contacts;
using Messenger.Domain.Chats;
using Messenger.Domain.Media;
using Messenger.Domain.Music;

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
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageSearchToken> MessageSearchTokens => Set<MessageSearchToken>();
    public DbSet<HiddenMessage> HiddenMessages => Set<HiddenMessage>();
    public DbSet<PinnedMessage> PinnedMessages => Set<PinnedMessage>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<StoredObject> StoredObjects => Set<StoredObject>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<MusicTrack> MusicTracks => Set<MusicTrack>();
    public DbSet<RightsDeclaration> RightsDeclarations => Set<RightsDeclaration>();
    public DbSet<UserMusicTrack> UserMusicTracks => Set<UserMusicTrack>();
    public DbSet<CopyrightClaim> CopyrightClaims => Set<CopyrightClaim>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MessengerDbContext).Assembly);
}
