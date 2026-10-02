namespace Messenger.Application.Users;

public interface IProfileService
{
    Task UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
    Task UpdateAvatarAsync(Guid userId, string? avatarObjectId, CancellationToken cancellationToken = default);
    Task UpdatePrivacyAsync(Guid userId, PrivacyUpdateRequest request, CancellationToken cancellationToken = default);
    Task<PrivacySettingsResult> GetPrivacyAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<VisibleProfile> GetProfileAsync(Guid targetUserId, Guid requesterUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
    Task ChangePhoneAsync(Guid userId, Guid challengeId, string code, CancellationToken cancellationToken = default);
}
