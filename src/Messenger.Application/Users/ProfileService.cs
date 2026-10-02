using Messenger.Application.Security;
using Messenger.Application.Identity;
using Messenger.Domain.Common;
using Messenger.Domain.Identity;
using Messenger.Domain.Users;

namespace Messenger.Application.Users;

public sealed class ProfileService(
    IUserSettingsStore store,
    IFieldCipher fieldCipher,
    IBlindIndex blindIndex,
    IChallengeCodeHasher codeHasher,
    IClock clock) : IProfileService
{
    public async Task UpdateProfileAsync(
        Guid userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireUserAsync(userId, cancellationToken);
        var normalized = string.IsNullOrWhiteSpace(request.Username)
            ? null
            : request.Username.Trim().ToUpperInvariant();
        if (normalized is not null &&
            await store.UsernameExistsAsync(normalized, userId, cancellationToken))
        {
            throw new UserSettingsException(UserSettingsError.UsernameTaken);
        }

        var profile = await GetOrCreateProfileAsync(userId, cancellationToken);
        profile.Update(request.FirstName, request.LastName, request.Username, request.Bio);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAvatarAsync(
        Guid userId,
        string? avatarObjectId,
        CancellationToken cancellationToken = default)
    {
        await RequireUserAsync(userId, cancellationToken);
        var profile = await GetOrCreateProfileAsync(userId, cancellationToken);
        profile.SetAvatar(avatarObjectId);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdatePrivacyAsync(
        Guid userId,
        PrivacyUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireUserAsync(userId, cancellationToken);
        var privacy = await store.FindPrivacyAsync(userId, cancellationToken);
        if (privacy is null)
        {
            privacy = new UserPrivacySettings(userId);
            await store.AddPrivacyAsync(privacy, cancellationToken);
        }

        privacy.Update(request.Phone, request.Avatar, request.LastSeen);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<PrivacySettingsResult> GetPrivacyAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await RequireUserAsync(userId, cancellationToken);
        var settings = await store.FindPrivacyAsync(userId, cancellationToken)
            ?? new UserPrivacySettings(userId);
        return new PrivacySettingsResult(
            settings.PhoneVisibility,
            settings.AvatarVisibility,
            settings.LastSeenVisibility);
    }

    public async Task<VisibleProfile> GetProfileAsync(
        Guid targetUserId,
        Guid requesterUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(targetUserId, cancellationToken);
        var profile = await GetOrCreateProfileAsync(targetUserId, cancellationToken);
        var privacy = await store.FindPrivacyAsync(targetUserId, cancellationToken)
            ?? new UserPrivacySettings(targetUserId);
        var isOwner = targetUserId == requesterUserId;
        var isContact = isOwner || await store.AreContactsAsync(targetUserId, requesterUserId, cancellationToken);

        return new VisibleProfile(
            targetUserId,
            profile.FirstName,
            profile.LastName,
            profile.Username,
            profile.Bio,
            IsVisible(privacy.PhoneVisibility, isOwner, isContact) ? DecryptPhone(user) : null,
            IsVisible(privacy.AvatarVisibility, isOwner, isContact) ? profile.AvatarObjectId : null,
            IsVisible(privacy.LastSeenVisibility, isOwner, isContact) ? profile.LastSeenAt : null);
    }

    public async Task<bool> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await store.FindOwnedSessionAsync(userId, sessionId, cancellationToken);
        if (session is null)
        {
            return false;
        }

        session.Revoke(clock.UtcNow, "session-revoked");
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        (await store.ListSessionsAsync(userId, cancellationToken))
        .Select(session => new SessionSummary(
            session.Id,
            session.DeviceLabel,
            session.CreatedAt,
            session.ExpiresAt,
            session.IsActive(clock.UtcNow)))
        .ToArray();

    public async Task ChangePhoneAsync(
        Guid userId,
        Guid challengeId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        var challenge = await store.FindChallengeAsync(challengeId, cancellationToken);
        if (challenge is null ||
            challenge.Purpose != ChallengePurpose.PhoneChange)
        {
            throw new UserSettingsException(UserSettingsError.RecentChallengeRequired);
        }

        var validation = challenge.Validate(codeHasher.Compute(challenge.Id, code), clock.UtcNow);
        if (validation != ChallengeValidation.Valid)
        {
            await store.SaveChangesAsync(cancellationToken);
            throw new UserSettingsException(validation switch
            {
                ChallengeValidation.InvalidCode => UserSettingsError.InvalidCode,
                ChallengeValidation.TooManyAttempts => UserSettingsError.TooManyAttempts,
                ChallengeValidation.Expired => UserSettingsError.CodeExpired,
                _ => UserSettingsError.RecentChallengeRequired
            });
        }

        var encrypted = new EncryptedValue(
            challenge.PhoneCiphertext,
            challenge.PhoneNonce,
            challenge.PhoneTag,
            challenge.PhoneKeyVersion);
        var phone = fieldCipher.Decrypt(encrypted);
        user.ChangePhone(
            challenge.PhoneCiphertext,
            challenge.PhoneNonce,
            challenge.PhoneTag,
            challenge.PhoneKeyVersion,
            blindIndex.Compute(phone));
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await store.FindUserAsync(userId, cancellationToken)
        ?? throw new UserSettingsException(UserSettingsError.UserNotFound);

    private async Task<UserProfile> GetOrCreateProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await store.FindProfileAsync(userId, cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        profile = new UserProfile(userId);
        await store.AddProfileAsync(profile, cancellationToken);
        return profile;
    }

    private string DecryptPhone(User user) => fieldCipher.Decrypt(new EncryptedValue(
        user.PhoneCiphertext,
        user.PhoneNonce,
        user.PhoneTag,
        user.PhoneKeyVersion));

    private static bool IsVisible(PrivacyVisibility visibility, bool isOwner, bool isContact) =>
        isOwner || visibility == PrivacyVisibility.Everybody ||
        visibility == PrivacyVisibility.Contacts && isContact;
}
