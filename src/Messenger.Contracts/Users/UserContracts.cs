namespace Messenger.Contracts.Users;

public sealed record UpdateMeRequest(string FirstName, string LastName, string? Username, string? Bio);
public sealed record AvatarRequest(string? ObjectId);
public sealed record MeResponse(
    Guid UserId,
    string FirstName,
    string LastName,
    string? Username,
    string? Bio,
    string? Phone,
    string? AvatarObjectId,
    DateTimeOffset? LastSeenAt);
public sealed record PrivacyRequest(string Phone, string Avatar, string LastSeen);
public sealed record PrivacyResponse(string Phone, string Avatar, string LastSeen);
public sealed record SessionResponse(Guid Id, string DeviceLabel, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool IsActive);
public sealed record BeginEmailTwoFactorRequest(string Email);
public sealed record EmailChallengeResponse(Guid ChallengeId, DateTimeOffset ExpiresAt);
public sealed record ConfirmEmailTwoFactorRequest(Guid ChallengeId, string Code);
public sealed record ConfirmLoginTwoFactorRequest(string PendingToken, string Code);
public sealed record PendingTwoFactorResponse(bool RequiresTwoFactor, string PendingToken, DateTimeOffset ExpiresAt);
public sealed record BeginPhoneChangeRequest(string Phone, string DefaultRegion);
public sealed record ChangePhoneRequest(Guid ChallengeId, string Code);
