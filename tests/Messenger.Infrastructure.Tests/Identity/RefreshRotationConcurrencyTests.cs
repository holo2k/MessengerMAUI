using Messenger.Application.Identity;
using Messenger.Domain.Identity;
using Messenger.Infrastructure.Identity;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Messenger.Infrastructure.Tests.Identity;

public sealed class RefreshRotationConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("messenger_rotation_tests")
        .WithUsername("messenger")
        .WithPassword("messenger-tests-only")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Concurrent_rotation_has_one_success_and_reuse_revokes_replacement()
    {
        var now = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        await using (var seed = CreateContext())
        {
            seed.Users.Add(new User(userId, "cipher", "nonce", "tag", 1, "phone-index", now));
            seed.RefreshSessions.Add(new RefreshSession(
                Guid.NewGuid(), userId, "original", familyId, "presented-hash", now, now.AddDays(30)));
            await seed.SaveChangesAsync();
        }

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstStore = new EfIdentityStore(firstContext);
        var secondStore = new EfIdentityStore(secondContext);
        var firstReplacement = NewReplacement(userId, familyId, "replacement-one", now);
        var secondReplacement = NewReplacement(userId, familyId, "replacement-two", now);

        var rotations = await Task.WhenAll(
            firstStore.RotateRefreshTokenAsync("presented-hash", firstReplacement, now, default),
            secondStore.RotateRefreshTokenAsync("presented-hash", secondReplacement, now, default));

        Assert.Equal(1, rotations.Count(result => result.Status == RefreshRotationStatus.Success));
        Assert.Equal(1, rotations.Count(result => result.Status == RefreshRotationStatus.Reused));

        await using var verify = CreateContext();
        var family = await verify.RefreshSessions
            .Where(session => session.TokenFamilyId == familyId)
            .ToListAsync();
        Assert.Equal(2, family.Count);
        Assert.All(family, session => Assert.NotNull(session.RevokedAt));
        Assert.DoesNotContain(family, session => session.IsActive(now));
    }

    private MessengerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MessengerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new MessengerDbContext(options);
    }

    private static RefreshSession NewReplacement(
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), userId, "replacement", familyId, tokenHash, now, now.AddDays(30));
}
