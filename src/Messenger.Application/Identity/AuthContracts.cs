using Messenger.Domain.Identity;

namespace Messenger.Application.Identity;

public sealed record PhoneChallengeRequest(string Phone, string DefaultRegion, ChallengePurpose Purpose);

public sealed record PhoneChallengeResult(Guid ChallengeId, DateTimeOffset ExpiresAt);

public sealed record CompletePhoneChallengeRequest(Guid ChallengeId, string Code, string DeviceLabel);

public sealed record AuthSessionResult(
    Guid UserId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public enum AuthErrorCode
{
    ChallengeNotFound,
    ChallengeExpired,
    ChallengeConsumed,
    TooManyAttempts,
    InvalidCode,
    WrongChallengePurpose,
    PhoneAlreadyRegistered,
    PhoneNotRegistered,
    InvalidRefreshToken,
    RefreshTokenReused
}

public sealed class AuthException(AuthErrorCode code) : Exception(code.ToString())
{
    public AuthErrorCode Code { get; } = code;
}

public sealed class AuthOptions
{
    public TimeSpan ChallengeLifetime { get; init; } = TimeSpan.FromMinutes(5);
    public int MaximumChallengeAttempts { get; init; } = 5;
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);
    public string DevelopmentCode { get; init; } = "111111";
}

public enum RefreshRotationStatus
{
    Success,
    Invalid,
    Expired,
    Reused
}

public sealed record RefreshRotationOutcome(RefreshRotationStatus Status, RefreshSession? Replacement = null);
