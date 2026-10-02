using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Messenger.Application.Users;

namespace Messenger.Infrastructure.Users;

public sealed class OneTimeCodeGenerator : IOneTimeCodeGenerator
{
    public string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000)
        .ToString("D6", CultureInfo.InvariantCulture);
}

public sealed class PendingLoginTokenGenerator : IPendingLoginTokenGenerator
{
    public string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
