using Messenger.Application.Support;
using Messenger.Domain.Support;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Support;

public sealed class EfSupportStore(MessengerDbContext db) : ISupportStore
{
    public async Task AddTicketAsync(SupportTicket ticket, CancellationToken ct) => await db.SupportTickets.AddAsync(ticket, ct);
    public async Task AddMessageAsync(SupportMessage message, CancellationToken ct) => await db.SupportMessages.AddAsync(message, ct);
    public Task<SupportTicket?> FindTicketAsync(Guid id, CancellationToken ct) => db.SupportTickets.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<SupportTicket>> ListForOwnerAsync(Guid ownerId, CancellationToken ct) => await db.SupportTickets.Where(x => x.OwnerUserId == ownerId).OrderByDescending(x => x.CreatedAt).ToArrayAsync(ct);
    public async Task<IReadOnlyList<SupportTicket>> ListAllAsync(CancellationToken ct) => await db.SupportTickets.OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt).ToArrayAsync(ct);
    public async Task<IReadOnlyList<SupportMessage>> ListMessagesAsync(Guid ticketId, CancellationToken ct) => await db.SupportMessages.Where(x => x.TicketId == ticketId).OrderBy(x => x.CreatedAt).ToArrayAsync(ct);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
