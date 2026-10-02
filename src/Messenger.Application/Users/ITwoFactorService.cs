namespace Messenger.Application.Users;

public sealed record EmailSetupResult(Guid ChallengeId, DateTimeOffset ExpiresAt);
public sealed record PendingLoginResult(string PendingToken, DateTimeOffset ExpiresAt);
public sealed record VerifiedPendingLogin(Guid UserId, string DeviceLabel);

public interface ITwoFactorService
{
    Task<EmailSetupResult> BeginEmailSetupAsync(Guid userId, string email, CancellationToken cancellationToken = default);
    Task ConfirmEmailAsync(Guid userId, Guid challengeId, string code, CancellationToken cancellationToken = default);
    Task<PendingLoginResult> BeginLoginAsync(Guid userId, string deviceLabel, CancellationToken cancellationToken = default);
    Task<VerifiedPendingLogin> ConfirmLoginAsync(string pendingToken, string code, CancellationToken cancellationToken = default);
    Task DisableAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IEmailSender
{
    Task SendTwoFactorCodeAsync(string recipient, string code, CancellationToken cancellationToken);
}

public interface IOneTimeCodeGenerator
{
    string Generate();
}

public interface IPendingLoginTokenGenerator
{
    string Generate();
    string Hash(string token);
}
