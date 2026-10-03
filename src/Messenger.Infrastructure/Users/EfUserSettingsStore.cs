using Messenger.Application.Users;
using Messenger.Domain.Identity;
using Messenger.Domain.Users;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Users;

public sealed class EfUserSettingsStore(MessengerDbContext dbContext) : IUserSettingsStore, ITwoFactorStore
{
    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<UserProfile?> FindProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserProfiles.SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    public Task<UserPrivacySettings?> FindPrivacyAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserPrivacySettings.SingleOrDefaultAsync(settings => settings.UserId == userId, cancellationToken);

    public Task<bool> UsernameExistsAsync(string normalizedUsername, Guid exceptUserId, CancellationToken cancellationToken) =>
        dbContext.UserProfiles.AnyAsync(
            profile => profile.UserId != exceptUserId && profile.NormalizedUsername == normalizedUsername,
            cancellationToken);

    public Task<bool> AreContactsAsync(Guid firstUserId, Guid secondUserId, CancellationToken cancellationToken) =>
        dbContext.Contacts.AnyAsync(contact =>
            contact.OwnerUserId == firstUserId && contact.TargetUserId == secondUserId,
            cancellationToken);

    public Task<RefreshSession?> FindOwnedSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.RefreshSessions.SingleOrDefaultAsync(
            session => session.UserId == userId && session.Id == sessionId,
            cancellationToken);

    public async Task<IReadOnlyList<RefreshSession>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.RefreshSessions
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<LoginChallenge?> FindChallengeAsync(Guid challengeId, CancellationToken cancellationToken) =>
        dbContext.LoginChallenges.SingleOrDefaultAsync(challenge => challenge.Id == challengeId, cancellationToken);

    public async Task AddProfileAsync(UserProfile profile, CancellationToken cancellationToken) =>
        await dbContext.UserProfiles.AddAsync(profile, cancellationToken);

    public async Task AddPrivacyAsync(UserPrivacySettings settings, CancellationToken cancellationToken) =>
        await dbContext.UserPrivacySettings.AddAsync(settings, cancellationToken);

    public Task<UserSecuritySettings?> FindSecurityAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserSecuritySettings.SingleOrDefaultAsync(settings => settings.UserId == userId, cancellationToken);

    public Task<EmailCodeChallenge?> FindEmailChallengeAsync(Guid challengeId, CancellationToken cancellationToken) =>
        dbContext.EmailCodeChallenges.SingleOrDefaultAsync(challenge => challenge.Id == challengeId, cancellationToken);

    public Task<PendingLogin?> FindPendingLoginAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.PendingLogins.SingleOrDefaultAsync(login => login.TokenHash == tokenHash, cancellationToken);

    public async Task AddSecurityAsync(UserSecuritySettings settings, CancellationToken cancellationToken) =>
        await dbContext.UserSecuritySettings.AddAsync(settings, cancellationToken);

    public async Task AddEmailChallengeAsync(EmailCodeChallenge challenge, CancellationToken cancellationToken) =>
        await dbContext.EmailCodeChallenges.AddAsync(challenge, cancellationToken);

    public async Task AddPendingLoginAsync(PendingLogin pendingLogin, CancellationToken cancellationToken) =>
        await dbContext.PendingLogins.AddAsync(pendingLogin, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
