using System.Security.Cryptography;
using System.Text;
using Messenger.Domain.Common;

namespace Messenger.Domain.Identity;

public enum ChallengePurpose
{
    Register = 1,
    Login = 2,
    PhoneChange = 3
}

public enum ChallengeValidation
{
    Valid,
    InvalidCode,
    Expired,
    TooManyAttempts,
    Consumed
}

public sealed class LoginChallenge : Entity
{
    private LoginChallenge()
    {
    }

    public LoginChallenge(
        Guid id,
        ChallengePurpose purpose,
        string phoneCiphertext,
        string phoneNonce,
        string phoneTag,
        int phoneKeyVersion,
        string phoneBlindIndex,
        string codeHash,
        DateTimeOffset expiresAt,
        int maximumAttempts)
    {
        Id = id;
        Purpose = purpose;
        PhoneCiphertext = phoneCiphertext;
        PhoneNonce = phoneNonce;
        PhoneTag = phoneTag;
        PhoneKeyVersion = phoneKeyVersion;
        PhoneBlindIndex = phoneBlindIndex;
        CodeHash = codeHash;
        ExpiresAt = expiresAt;
        MaximumAttempts = maximumAttempts;
    }

    public ChallengePurpose Purpose { get; private set; }
    public string PhoneCiphertext { get; private set; } = string.Empty;
    public string PhoneNonce { get; private set; } = string.Empty;
    public string PhoneTag { get; private set; } = string.Empty;
    public int PhoneKeyVersion { get; private set; }
    public string PhoneBlindIndex { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public int MaximumAttempts { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public ChallengeValidation Validate(string candidateHash, DateTimeOffset now)
    {
        if (ConsumedAt is not null)
        {
            return ChallengeValidation.Consumed;
        }

        if (now >= ExpiresAt)
        {
            return ChallengeValidation.Expired;
        }

        if (Attempts >= MaximumAttempts)
        {
            return ChallengeValidation.TooManyAttempts;
        }

        if (!FixedTimeEquals(CodeHash, candidateHash))
        {
            Attempts++;
            return ChallengeValidation.InvalidCode;
        }

        ConsumedAt = now;
        return ChallengeValidation.Valid;
    }

    public void MarkConsumed(DateTimeOffset consumedAt) => ConsumedAt = consumedAt;

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
}
