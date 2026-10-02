using Microsoft.EntityFrameworkCore;
using Messenger.Domain.Identity;

namespace Messenger.Infrastructure.Persistence;

public sealed class MessengerDbContext(DbContextOptions<MessengerDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<LoginChallenge> LoginChallenges => Set<LoginChallenge>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MessengerDbContext).Assembly);
}
