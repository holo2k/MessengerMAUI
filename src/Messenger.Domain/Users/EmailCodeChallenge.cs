using System.Security.Cryptography;
using System.Text;
using Messenger.Domain.Common;
using Messenger.Domain.Identity;

namespace Messenger.Domain.Users;

public enum EmailCodePurpose
{
    Setup = 1,
    Login = 2
}

public sealed class EmailCodeChallenge : Entity
{
    private EmailCodeChallenge()
    {
    }

    public EmailCodeChallenge(
        Guid id,
        Guid userId,
        EmailCodePurpose purpose,
        string emailCiphertext,
        string emailNonce,
        string emailTag,
        int emailKeyVersion,
        string codeHash,
        DateTimeOffset expiresAt,
        int maximumAttempts)
    {
        Id = id;
        UserId = userId;
        Purpose = purpose;
        EmailCiphertext = emailCiphertext;
        EmailNonce = emailNonce;
        EmailTag = emailTag;
        EmailKeyVersion = emailKeyVersion;
        CodeHash = codeHash;
        ExpiresAt = expiresAt;
        MaximumAttempts = maximumAttempts;
    }

    public Guid UserId { get; private set; }
    public EmailCodePurpose Purpose { get; private set; }
    public string EmailCiphertext { get; private set; } = string.Empty;
    public string EmailNonce { get; private set; } = string.Empty;
    public string EmailTag { get; private set; } = string.Empty;
    public int EmailKeyVersion { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public int MaximumAttempts { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public ChallengeValidation Validate(string candidateHash, DateTimeOffset now)
    {
        if (ConsumedAt is not null) return ChallengeValidation.Consumed;
        if (now >= ExpiresAt) return ChallengeValidation.Expired;
        if (Attempts >= MaximumAttempts) return ChallengeValidation.TooManyAttempts;
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(CodeHash),
                Encoding.UTF8.GetBytes(candidateHash)))
        {
            Attempts++;
            return ChallengeValidation.InvalidCode;
        }

        ConsumedAt = now;
        return ChallengeValidation.Valid;
    }
}
