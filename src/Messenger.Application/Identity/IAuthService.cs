namespace Messenger.Application.Identity;

public interface IAuthService
{
    Task<PhoneChallengeResult> RequestChallengeAsync(
        PhoneChallengeRequest request,
        CancellationToken cancellationToken = default);

    Task<AuthSessionResult> RegisterAsync(
        CompletePhoneChallengeRequest request,
        CancellationToken cancellationToken = default);

    Task<AuthSessionResult> LoginAsync(
        CompletePhoneChallengeRequest request,
        CancellationToken cancellationToken = default);

    Task<AuthSessionResult> RefreshAsync(
        string refreshToken,
        string deviceLabel,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task LogoutAllAsync(Guid userId, CancellationToken cancellationToken = default);
}
