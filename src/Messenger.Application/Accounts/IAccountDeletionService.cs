using Messenger.Domain.Accounts;
using Messenger.Domain.Common;

namespace Messenger.Application.Accounts;

public interface IAccountDeletionStore
{
    Task<AccountDeletionRequest?> FindActiveAsync(Guid userId, CancellationToken ct);
    Task AddAsync(AccountDeletionRequest request, CancellationToken ct);
    Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<AccountDeletionRequest>> ListDueAsync(DateTimeOffset now, CancellationToken ct);
    Task AnonymizeAsync(Guid userId, DateTimeOffset now, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IAccountDeletionService
{
    Task<AccountDeletionRequest> RequestAsync(Guid userId, CancellationToken ct = default);
    Task CancelAsync(Guid userId, CancellationToken ct = default);
    Task<int> ProcessDueAsync(CancellationToken ct = default);
}

public sealed class AccountDeletionService(IAccountDeletionStore store, IClock clock) : IAccountDeletionService
{
    public async Task<AccountDeletionRequest> RequestAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await store.FindActiveAsync(userId, ct);
        if (existing is not null) return existing;
        var request = new AccountDeletionRequest(Guid.NewGuid(), userId, clock.UtcNow);
        await store.AddAsync(request, ct); await store.RevokeSessionsAsync(userId, clock.UtcNow, ct); await store.SaveChangesAsync(ct);
        return request;
    }
    public async Task CancelAsync(Guid userId, CancellationToken ct = default)
    {
        var request = await store.FindActiveAsync(userId, ct) ?? throw new AccountDeletionException(AccountDeletionError.NotFound);
        request.Cancel(clock.UtcNow); await store.SaveChangesAsync(ct);
    }
    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        var due = await store.ListDueAsync(clock.UtcNow, ct);
        foreach (var request in due) { await store.AnonymizeAsync(request.UserId, clock.UtcNow, ct); request.MarkExecuted(clock.UtcNow); }
        if (due.Count > 0) await store.SaveChangesAsync(ct);
        return due.Count;
    }
}
