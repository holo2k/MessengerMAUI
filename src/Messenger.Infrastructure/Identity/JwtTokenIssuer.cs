using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Messenger.Application.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Messenger.Infrastructure.Identity;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
}

public sealed class JwtTokenIssuer : IAccessTokenIssuer
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;

    public JwtTokenIssuer(JwtOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        var key = Convert.FromBase64String(options.SigningKey);
        if (key.Length < 32)
        {
            throw new ArgumentException("The JWT signing key must contain at least 256 bits.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new ArgumentException("JWT issuer and audience are required.", nameof(options));
        }

        _key = new SymmetricSecurityKey(key);
    }

    public string Issue(Guid userId, DateTimeOffset expiresAt)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            Expires = expiresAt.UtcDateTime,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
