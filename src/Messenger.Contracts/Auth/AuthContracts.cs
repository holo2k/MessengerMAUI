namespace Messenger.Contracts.Auth;

public sealed record ChallengeRequest(string Phone, string DefaultRegion, string Purpose);

public sealed record ChallengeResponse(Guid ChallengeId, DateTimeOffset ExpiresAt);

public sealed record CompleteChallengeRequest(Guid ChallengeId, string Code, string DeviceLabel);

public sealed record RefreshRequest(string RefreshToken, string DeviceLabel);

public sealed record AuthSessionResponse(
    Guid UserId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
