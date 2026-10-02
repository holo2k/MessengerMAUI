using Messenger.Domain.Common;
using Messenger.Domain.Identity;

namespace Messenger.Application.Identity;

public interface ISmsSender
{
    Task SendCodeAsync(PhoneNumber phone, string code, CancellationToken cancellationToken);
}

public interface IChallengeCodeHasher
{
    string Compute(Guid challengeId, string code);
}

public interface IAccessTokenIssuer
{
    string Issue(Guid userId, DateTimeOffset expiresAt);
}

public interface IRefreshTokenGenerator
{
    string Generate();
    string Hash(string token);
}

public interface IIdentityStore
{
    Task AddChallengeAsync(LoginChallenge challenge, CancellationToken cancellationToken);
    Task<LoginChallenge?> FindChallengeAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> FindUserByPhoneIndexAsync(string phoneIndex, CancellationToken cancellationToken);
    Task AddUserAsync(User user, CancellationToken cancellationToken);
    Task AddRefreshSessionAsync(RefreshSession session, CancellationToken cancellationToken);
    Task<RefreshSession?> FindRefreshSessionAsync(string tokenHash, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<RefreshRotationOutcome> RotateRefreshTokenAsync(
        string presentedTokenHash,
        RefreshSession replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task RevokeSessionAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeAllSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
