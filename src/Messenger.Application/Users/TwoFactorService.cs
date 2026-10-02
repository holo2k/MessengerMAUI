using Messenger.Application.Identity;
using Messenger.Application.Security;
using Messenger.Domain.Common;
using Messenger.Domain.Identity;
using Messenger.Domain.Users;

namespace Messenger.Application.Users;

public sealed class TwoFactorOptions
{
    public TimeSpan CodeLifetime { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan PendingLoginLifetime { get; init; } = TimeSpan.FromMinutes(5);
    public int MaximumAttempts { get; init; } = 5;
}

public sealed class TwoFactorService(
    ITwoFactorStore store,
    IEmailSender emailSender,
    IFieldCipher fieldCipher,
    IChallengeCodeHasher codeHasher,
    IOneTimeCodeGenerator codeGenerator,
    IPendingLoginTokenGenerator tokenGenerator,
    IClock clock,
    TwoFactorOptions options) : ITwoFactorService
{
    public async Task<EmailSetupResult> BeginEmailSetupAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken = default)
    {
        var challenge = CreateChallenge(userId, EmailCodePurpose.Setup, email, out var code);
        await store.AddEmailChallengeAsync(challenge, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await emailSender.SendTwoFactorCodeAsync(email, code, cancellationToken);
        return new EmailSetupResult(challenge.Id, challenge.ExpiresAt);
    }

    public async Task ConfirmEmailAsync(
        Guid userId,
        Guid challengeId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var challenge = await store.FindEmailChallengeAsync(challengeId, cancellationToken);
        if (challenge is null || challenge.UserId != userId || challenge.Purpose != EmailCodePurpose.Setup)
        {
            throw new UserSettingsException(UserSettingsError.InvalidCode);
        }

        ValidateCode(challenge, code);
        var security = await store.FindSecurityAsync(userId, cancellationToken);
        if (security is null)
        {
            security = new UserSecuritySettings(userId);
            await store.AddSecurityAsync(security, cancellationToken);
        }

        security.Enable(
            challenge.EmailCiphertext,
            challenge.EmailNonce,
            challenge.EmailTag,
            challenge.EmailKeyVersion);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<PendingLoginResult> BeginLoginAsync(
        Guid userId,
        string deviceLabel,
        CancellationToken cancellationToken = default)
    {
        var security = await store.FindSecurityAsync(userId, cancellationToken);
        if (security is not { TwoFactorEnabled: true, EmailCiphertext: not null, EmailNonce: not null, EmailTag: not null, EmailKeyVersion: not null })
        {
            throw new UserSettingsException(UserSettingsError.TwoFactorNotEnabled);
        }

        var email = fieldCipher.Decrypt(new EncryptedValue(
            security.EmailCiphertext,
            security.EmailNonce,
            security.EmailTag,
            security.EmailKeyVersion.Value));
        var challenge = CreateChallenge(userId, EmailCodePurpose.Login, email, out var code);
        var token = tokenGenerator.Generate();
        var pending = new PendingLogin(
            Guid.NewGuid(), userId, challenge.Id, deviceLabel, tokenGenerator.Hash(token),
            clock.UtcNow.Add(options.PendingLoginLifetime));
        await store.AddEmailChallengeAsync(challenge, cancellationToken);
        await store.AddPendingLoginAsync(pending, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await emailSender.SendTwoFactorCodeAsync(email, code, cancellationToken);
        return new PendingLoginResult(token, pending.ExpiresAt);
    }

    public async Task<VerifiedPendingLogin> ConfirmLoginAsync(
        string pendingToken,
        string code,
        CancellationToken cancellationToken = default)
    {
        var pending = await store.FindPendingLoginAsync(tokenGenerator.Hash(pendingToken), cancellationToken);
        if (pending is null)
        {
            throw new UserSettingsException(UserSettingsError.InvalidPendingToken);
        }

        if (pending.ConsumedAt is not null)
        {
            throw new UserSettingsException(UserSettingsError.CodeConsumed);
        }

        if (!pending.CanConsume(clock.UtcNow))
        {
            throw new UserSettingsException(UserSettingsError.CodeExpired);
        }

        var challenge = await store.FindEmailChallengeAsync(pending.EmailChallengeId, cancellationToken)
            ?? throw new UserSettingsException(UserSettingsError.InvalidCode);
        ValidateCode(challenge, code);
        pending.Consume(clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return new VerifiedPendingLogin(pending.UserId, pending.DeviceLabel);
    }

    public async Task DisableAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var security = await store.FindSecurityAsync(userId, cancellationToken);
        security?.Disable();
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await store.FindSecurityAsync(userId, cancellationToken))?.TwoFactorEnabled == true;

    private EmailCodeChallenge CreateChallenge(
        Guid userId,
        EmailCodePurpose purpose,
        string email,
        out string code)
    {
        var id = Guid.NewGuid();
        code = codeGenerator.Generate();
        var encrypted = fieldCipher.Encrypt(email.Trim());
        return new EmailCodeChallenge(
            id, userId, purpose, encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag,
            encrypted.KeyVersion, codeHasher.Compute(id, code),
            clock.UtcNow.Add(options.CodeLifetime), options.MaximumAttempts);
    }

    private void ValidateCode(EmailCodeChallenge challenge, string code)
    {
        var validation = challenge.Validate(codeHasher.Compute(challenge.Id, code), clock.UtcNow);
        if (validation == ChallengeValidation.Valid)
        {
            return;
        }

        throw new UserSettingsException(validation switch
        {
            ChallengeValidation.Consumed => UserSettingsError.CodeConsumed,
            ChallengeValidation.Expired => UserSettingsError.CodeExpired,
            ChallengeValidation.TooManyAttempts => UserSettingsError.TooManyAttempts,
            _ => UserSettingsError.InvalidCode
        });
    }
}
