using System.Security.Cryptography;
using System.Text;
using Messenger.Application.Identity;

namespace Messenger.Infrastructure.Identity;

public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    public string Generate() => Base64Url(RandomNumberGenerator.GetBytes(64));

    public string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
