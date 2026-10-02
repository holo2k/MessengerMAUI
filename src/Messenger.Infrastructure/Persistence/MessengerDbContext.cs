using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Persistence;

public sealed class MessengerDbContext(DbContextOptions<MessengerDbContext> options)
    : DbContext(options);
