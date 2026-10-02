using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Messenger.Infrastructure.Tests.Persistence;

public sealed class SchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("messenger_tests")
        .WithUsername("messenger")
        .WithPassword("messenger-tests-only")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Foundation_migration_applies_to_empty_database_and_is_idempotent()
    {
        var options = new DbContextOptionsBuilder<MessengerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using (var firstContext = new MessengerDbContext(options))
        {
            await firstContext.Database.MigrateAsync();
            var applied = await firstContext.Database.GetAppliedMigrationsAsync();
            Assert.Contains("00000000000000_Foundation", applied);
        }

        await using (var secondContext = new MessengerDbContext(options))
        {
            var beforeSecondApply = (await secondContext.Database.GetAppliedMigrationsAsync()).ToArray();
            await secondContext.Database.MigrateAsync();
            Assert.Equal(beforeSecondApply, await secondContext.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await secondContext.Database.GetPendingMigrationsAsync());
        }
    }
}
