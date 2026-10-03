using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Messenger.Infrastructure.Persistence;

public sealed class DesignTimeMessengerDbContextFactory : IDesignTimeDbContextFactory<MessengerDbContext>
{
    public MessengerDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Messenger")
            ?? "Host=localhost;Port=5432;Database=messenger;Username=messenger;Password=messenger-local-only";
        return new MessengerDbContext(new DbContextOptionsBuilder<MessengerDbContext>().UseNpgsql(connection).Options);
    }
}
