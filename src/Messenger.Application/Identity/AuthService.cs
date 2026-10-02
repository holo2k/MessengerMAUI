using Messenger.Application.Security;
using Messenger.Domain.Common;
using Messenger.Domain.Identity;

namespace Messenger.Application.Identity;

public sealed class AuthService(
    IIdentityStore store,
    IFieldCipher fieldCipher,
    IBlindIndex blindIndex,
    IChallengeCodeHasher codeHasher,
    ISmsSender smsSender,
    IAccessTokenIssuer accessTokenIssuer,
    IRefreshTokenGenerator refreshTokenGenerator,
    IClock clock,
    AuthOptions options) : IAuthService
{
    public async Task<PhoneChallengeResult> RequestChallengeAsync(
        PhoneChallengeRequest request,
        CancellationToken cancellationToken = default)
    {
        var phone = PhoneNumber.Parse(request.Phone, request.DefaultRegion);
        var id = Guid.NewGuid();
        var encryptedPhone = fieldCipher.Encrypt(phone.E164);
        var expiresAt = clock.UtcNow.Add(options.ChallengeLifetime);
        var challenge = new LoginChallenge(
            id,
            request.Purpose,
            encryptedPhone.Ciphertext,
            encryptedPhone.Nonce,
            encryptedPhone.Tag,
            encryptedPhone.KeyVersion,
            blindIndex.Compute(phone.E164),
            codeHasher.Compute(id, options.DevelopmentCode),
            expiresAt,
            options.MaximumChallengeAttempts);

        await store.AddChallengeAsync(challenge, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await smsSender.SendCodeAsync(phone, options.DevelopmentCode, cancellationToken);
        return new PhoneChallengeResult(id, expiresAt);
    }

    public Task<AuthSessionResult> RegisterAsync(
        CompletePhoneChallengeRequest request,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(request, ChallengePurpose.Register, createUser: true, cancellationToken);

    public Task<AuthSessionResult> LoginAsync(
        CompletePhoneChallengeRequest request,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(request, ChallengePurpose.Login, createUser: false, cancellationToken);

    public async Task<AuthSessionResult> RefreshAsync(
        string refreshToken,
        string deviceLabel,
        CancellationToken cancellationToken = default)
    {
        var presentedHash = refreshTokenGenerator.Hash(refreshToken);
        var current = await store.FindRefreshSessionAsync(presentedHash, cancellationToken)
            ?? throw new AuthException(AuthErrorCode.InvalidRefreshToken);
        if (current.RevokedAt is not null && current.ReplacedBySessionId is null)
        {
            throw new AuthException(AuthErrorCode.InvalidRefreshToken);
        }
        var newToken = refreshTokenGenerator.Generate();
        var replacement = new RefreshSession(
            Guid.NewGuid(),
            current.UserId,
            deviceLabel,
            current.TokenFamilyId,
            refreshTokenGenerator.Hash(newToken),
            clock.UtcNow,
            clock.UtcNow.Add(options.RefreshTokenLifetime));
        var outcome = await store.RotateRefreshTokenAsync(
            presentedHash, replacement, clock.UtcNow, cancellationToken);

        return outcome.Status switch
        {
            RefreshRotationStatus.Success => IssueResult(current.UserId, newToken, replacement.ExpiresAt),
            RefreshRotationStatus.Reused => throw new AuthException(AuthErrorCode.RefreshTokenReused),
            _ => throw new AuthException(AuthErrorCode.InvalidRefreshToken)
        };
    }

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        store.RevokeSessionAsync(refreshTokenGenerator.Hash(refreshToken), clock.UtcNow, cancellationToken);

    public Task LogoutAllAsync(Guid userId, CancellationToken cancellationToken = default) =>
        store.RevokeAllSessionsAsync(userId, clock.UtcNow, cancellationToken);

    private async Task<AuthSessionResult> CompleteAsync(
        CompletePhoneChallengeRequest request,
        ChallengePurpose expectedPurpose,
        bool createUser,
        CancellationToken cancellationToken)
    {
        var challenge = await store.FindChallengeAsync(request.ChallengeId, cancellationToken)
            ?? throw new AuthException(AuthErrorCode.ChallengeNotFound);
        if (challenge.Purpose != expectedPurpose)
        {
            throw new AuthException(AuthErrorCode.WrongChallengePurpose);
        }

        var validation = challenge.Validate(codeHasher.Compute(challenge.Id, request.Code), clock.UtcNow);
        if (validation != ChallengeValidation.Valid)
        {
            await store.SaveChangesAsync(cancellationToken);
            throw new AuthException(validation switch
            {
                ChallengeValidation.Expired => AuthErrorCode.ChallengeExpired,
                ChallengeValidation.Consumed => AuthErrorCode.ChallengeConsumed,
                ChallengeValidation.TooManyAttempts => AuthErrorCode.TooManyAttempts,
                _ => AuthErrorCode.InvalidCode
            });
        }

        var user = await store.FindUserByPhoneIndexAsync(challenge.PhoneBlindIndex, cancellationToken);
        if (createUser && user is not null)
        {
            await store.SaveChangesAsync(cancellationToken);
            throw new AuthException(AuthErrorCode.PhoneAlreadyRegistered);
        }

        if (!createUser && user is null)
        {
            await store.SaveChangesAsync(cancellationToken);
            throw new AuthException(AuthErrorCode.PhoneNotRegistered);
        }

        if (user is null)
        {
            user = new User(
                Guid.NewGuid(),
                challenge.PhoneCiphertext,
                challenge.PhoneNonce,
                challenge.PhoneTag,
                challenge.PhoneKeyVersion,
                challenge.PhoneBlindIndex,
                clock.UtcNow);
            await store.AddUserAsync(user, cancellationToken);
        }

        var refreshToken = refreshTokenGenerator.Generate();
        var refreshExpiresAt = clock.UtcNow.Add(options.RefreshTokenLifetime);
        await store.AddRefreshSessionAsync(
            new RefreshSession(
                Guid.NewGuid(),
                user.Id,
                request.DeviceLabel,
                Guid.NewGuid(),
                refreshTokenGenerator.Hash(refreshToken),
                clock.UtcNow,
                refreshExpiresAt),
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return IssueResult(user.Id, refreshToken, refreshExpiresAt);
    }

    private AuthSessionResult IssueResult(Guid userId, string refreshToken, DateTimeOffset refreshExpiresAt)
    {
        var accessExpiresAt = clock.UtcNow.Add(options.AccessTokenLifetime);
        return new AuthSessionResult(
            userId,
            accessTokenIssuer.Issue(userId, accessExpiresAt),
            accessExpiresAt,
            refreshToken,
            refreshExpiresAt);
    }
}
