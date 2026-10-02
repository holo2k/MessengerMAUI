using System.Security.Cryptography;
using System.Text;
using Messenger.Application.Security;

namespace Messenger.Infrastructure.Security;

public sealed class HmacBlindIndex : IBlindIndex
{
    private const int MinimumKeySize = 32;

    private readonly byte[] _key;

    public HmacBlindIndex(BlindIndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            _key = Convert.FromBase64String(options.Key);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The blind-index key is not valid Base64.", nameof(options), exception);
        }

        if (_key.Length < MinimumKeySize)
        {
            throw new ArgumentException("The blind-index key must contain at least 256 bits.", nameof(options));
        }
    }

    public string Compute(string normalizedValue)
    {
        ArgumentNullException.ThrowIfNull(normalizedValue);

        var digest = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(normalizedValue));
        return Convert.ToHexStringLower(digest);
    }
}
