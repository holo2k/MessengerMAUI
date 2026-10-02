using Messenger.Domain.Users;

namespace Messenger.Application.Users;

public interface ITwoFactorStore
{
    Task<UserSecuritySettings?> FindSecurityAsync(Guid userId, CancellationToken cancellationToken);
    Task<EmailCodeChallenge?> FindEmailChallengeAsync(Guid challengeId, CancellationToken cancellationToken);
    Task<PendingLogin?> FindPendingLoginAsync(string tokenHash, CancellationToken cancellationToken);
    Task AddSecurityAsync(UserSecuritySettings settings, CancellationToken cancellationToken);
    Task AddEmailChallengeAsync(EmailCodeChallenge challenge, CancellationToken cancellationToken);
    Task AddPendingLoginAsync(PendingLogin pendingLogin, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
