using System.Data;
using Messenger.Application.Identity;
using Messenger.Domain.Identity;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Identity;

public sealed class EfIdentityStore(MessengerDbContext dbContext) : IIdentityStore
{
    public async Task AddChallengeAsync(LoginChallenge challenge, CancellationToken cancellationToken) =>
        await dbContext.LoginChallenges.AddAsync(challenge, cancellationToken);

    public Task<LoginChallenge?> FindChallengeAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.LoginChallenges.SingleOrDefaultAsync(challenge => challenge.Id == id, cancellationToken);

    public Task<User?> FindUserByPhoneIndexAsync(string phoneIndex, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(user => user.PhoneBlindIndex == phoneIndex, cancellationToken);

    public async Task AddUserAsync(User user, CancellationToken cancellationToken) =>
        await dbContext.Users.AddAsync(user, cancellationToken);

    public async Task AddRefreshSessionAsync(RefreshSession session, CancellationToken cancellationToken) =>
        await dbContext.RefreshSessions.AddAsync(session, cancellationToken);

    public Task<RefreshSession?> FindRefreshSessionAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.RefreshSessions.AsNoTracking().SingleOrDefaultAsync(
            session => session.TokenHash == tokenHash,
            cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);

    public async Task<RefreshRotationOutcome> RotateRefreshTokenAsync(
        string presentedTokenHash,
        RefreshSession replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var current = await dbContext.RefreshSessions
            .FromSqlInterpolated($"SELECT * FROM refresh_sessions WHERE token_hash = {presentedTokenHash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new RefreshRotationOutcome(RefreshRotationStatus.Invalid);
        }

        if (current.ReplacedBySessionId is not null || current.RevokedAt is not null)
        {
            var family = await dbContext.RefreshSessions
                .Where(session => session.TokenFamilyId == current.TokenFamilyId)
                .ToListAsync(cancellationToken);
            foreach (var session in family)
            {
                session.Revoke(now, "refresh-token-reuse");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RefreshRotationOutcome(RefreshRotationStatus.Reused);
        }

        if (current.ExpiresAt <= now)
        {
            current.Revoke(now, "expired");
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RefreshRotationOutcome(RefreshRotationStatus.Expired);
        }

        current.ReplaceWith(replacement.Id);
        await dbContext.RefreshSessions.AddAsync(replacement, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RefreshRotationOutcome(RefreshRotationStatus.Success, replacement);
    }

    public async Task RevokeSessionAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.RefreshSessions.SingleOrDefaultAsync(
            item => item.TokenHash == tokenHash,
            cancellationToken);
        if (session is not null)
        {
            session.Revoke(now, "logout");
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RevokeAllSessionsAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.RefreshSessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(now, "logout-all");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
