namespace Messenger.Application.Users;

public sealed record UpdateProfileRequest(string FirstName, string LastName, string? Username, string? Bio);

public sealed record PrivacyUpdateRequest(
    Messenger.Domain.Users.PrivacyVisibility Phone,
    Messenger.Domain.Users.PrivacyVisibility Avatar,
    Messenger.Domain.Users.PrivacyVisibility LastSeen);

public sealed record PrivacySettingsResult(
    Messenger.Domain.Users.PrivacyVisibility Phone,
    Messenger.Domain.Users.PrivacyVisibility Avatar,
    Messenger.Domain.Users.PrivacyVisibility LastSeen);

public sealed record VisibleProfile(
    Guid UserId,
    string FirstName,
    string LastName,
    string? Username,
    string? Bio,
    string? Phone,
    string? AvatarObjectId,
    DateTimeOffset? LastSeenAt);

public sealed record SessionSummary(
    Guid Id,
    string DeviceLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsActive);

public enum UserSettingsError
{
    UserNotFound,
    UsernameTaken,
    RecentChallengeRequired,
    CodeConsumed,
    CodeExpired,
    InvalidCode,
    TooManyAttempts,
    TwoFactorNotEnabled,
    InvalidPendingToken
}

public sealed class UserSettingsException(UserSettingsError code) : Exception(code.ToString())
{
    public UserSettingsError Code { get; } = code;
}
