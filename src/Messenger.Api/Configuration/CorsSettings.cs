namespace Messenger.Api.Configuration;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "MessengerCors";

    public string[] AllowedOrigins { get; init; } = [];
}
