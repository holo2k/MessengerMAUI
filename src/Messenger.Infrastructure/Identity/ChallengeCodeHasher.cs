using System.Security.Cryptography;
using System.Text;
using Messenger.Application.Identity;

namespace Messenger.Infrastructure.Identity;

public sealed class ChallengeHashOptions
{
    public string Key { get; init; } = string.Empty;
}

public sealed class ChallengeCodeHasher : IChallengeCodeHasher
{
    private readonly byte[] _key;

    public ChallengeCodeHasher(ChallengeHashOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _key = Convert.FromBase64String(options.Key);
        if (_key.Length < 32)
        {
            throw new ArgumentException("The challenge hash key must contain at least 256 bits.", nameof(options));
        }
    }

    public string Compute(Guid challengeId, string code)
    {
        var value = Encoding.UTF8.GetBytes($"{challengeId:N}:{code}");
        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, value));
    }
}
