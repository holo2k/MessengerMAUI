using Messenger.Application.Accounts;

namespace Messenger.Api.Background;

public sealed class AccountDeletionWorker(IServiceScopeFactory scopes, ILogger<AccountDeletionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var count = await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().ProcessDueAsync(stoppingToken);
                if (count > 0) logger.LogInformation("Processed {Count} due account deletion requests", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Account deletion processing failed"); }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
