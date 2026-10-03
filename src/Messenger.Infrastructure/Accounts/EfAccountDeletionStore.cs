using Messenger.Application.Accounts;
using Messenger.Domain.Accounts;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Accounts;

public sealed class EfAccountDeletionStore(MessengerDbContext db) : IAccountDeletionStore
{
    public Task<AccountDeletionRequest?> FindActiveAsync(Guid userId, CancellationToken ct) => db.AccountDeletionRequests.SingleOrDefaultAsync(x => x.UserId == userId && x.CancelledAt == null && x.ExecutedAt == null, ct);
    public async Task AddAsync(AccountDeletionRequest request, CancellationToken ct) => await db.AccountDeletionRequests.AddAsync(request, ct);
    public async Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var sessions = await db.RefreshSessions.Where(x => x.UserId == userId && x.RevokedAt == null).ToArrayAsync(ct);
        foreach (var session in sessions) session.Revoke(now, "account_deletion_requested");
    }
    public async Task<IReadOnlyList<AccountDeletionRequest>> ListDueAsync(DateTimeOffset now, CancellationToken ct) => await db.AccountDeletionRequests.Where(x => x.CancelledAt == null && x.ExecutedAt == null && x.ExecuteAt <= now).ToArrayAsync(ct);
    public async Task AnonymizeAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        var profile = await db.UserProfiles.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        user?.Deactivate(now);
        profile?.Anonymize();
    }
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
