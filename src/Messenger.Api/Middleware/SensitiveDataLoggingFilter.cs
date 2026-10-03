using System.Text.RegularExpressions;

namespace Messenger.Api.Middleware;

public static partial class SensitiveDataLoggingFilter
{
    public static string Redact(string value)
    {
        var withoutBearer = BearerPattern().Replace(value, "Bearer [REDACTED]");
        return SecretPattern().Replace(withoutBearer, match => $"{match.Groups[1].Value}=[REDACTED]");
    }

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerPattern();
    [GeneratedRegex(@"(?i)\b(accessToken|refreshToken|password|smtpPassword|secretKey)\s*[:=]\s*[^\s,;]+")]
    private static partial Regex SecretPattern();
}
