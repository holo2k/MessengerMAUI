namespace Messenger.Api.Configuration;

public sealed class SmsProviderStartupValidator(
    IHostEnvironment environment,
    IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var provider = configuration["Sms:Provider"] ?? "Development";
        if (!environment.IsDevelopment() &&
            string.Equals(provider, "Development", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DevelopmentSmsSender cannot be used outside Development.");
        }

        if (!string.Equals(provider, "Development", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SMS provider '{provider}' is not configured.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
