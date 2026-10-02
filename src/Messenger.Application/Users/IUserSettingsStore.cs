using Messenger.Domain.Identity;
using Messenger.Domain.Users;

namespace Messenger.Application.Users;

public interface IUserSettingsStore
{
    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserProfile?> FindProfileAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserPrivacySettings?> FindPrivacyAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> UsernameExistsAsync(string normalizedUsername, Guid exceptUserId, CancellationToken cancellationToken);
    Task<bool> AreContactsAsync(Guid firstUserId, Guid secondUserId, CancellationToken cancellationToken);
    Task<RefreshSession?> FindOwnedSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RefreshSession>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken);
    Task<LoginChallenge?> FindChallengeAsync(Guid challengeId, CancellationToken cancellationToken);
    Task AddProfileAsync(UserProfile profile, CancellationToken cancellationToken);
    Task AddPrivacyAsync(UserPrivacySettings settings, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
