using Messenger.Domain.Common;
using Messenger.Domain.Support;

namespace Messenger.Application.Support;

public sealed class SupportAdminOptions { public HashSet<Guid> UserIds { get; set; } = []; }
public sealed record SupportConversation(SupportTicket Ticket, IReadOnlyList<SupportMessage> Messages);

public interface ISupportStore
{
    Task AddTicketAsync(SupportTicket ticket, CancellationToken ct);
    Task AddMessageAsync(SupportMessage message, CancellationToken ct);
    Task<SupportTicket?> FindTicketAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<SupportTicket>> ListForOwnerAsync(Guid ownerId, CancellationToken ct);
    Task<IReadOnlyList<SupportTicket>> ListAllAsync(CancellationToken ct);
    Task<IReadOnlyList<SupportMessage>> ListMessagesAsync(Guid ticketId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface ISupportService
{
    Task<SupportConversation> CreateAsync(Guid actorId, string subject, string body, CancellationToken ct = default);
    Task<IReadOnlyList<SupportTicket>> ListAsync(Guid actorId, CancellationToken ct = default);
    Task<SupportConversation> GetAsync(Guid actorId, Guid ticketId, CancellationToken ct = default);
    Task ReplyAsync(Guid actorId, Guid ticketId, string body, CancellationToken ct = default);
    Task<IReadOnlyList<SupportTicket>> ListAllAsync(Guid administratorId, CancellationToken ct = default);
    Task ReplyAsStaffAsync(Guid administratorId, Guid ticketId, string body, CancellationToken ct = default);
    Task CloseAsync(Guid administratorId, Guid ticketId, CancellationToken ct = default);
}

public sealed class SupportService(ISupportStore store, IClock clock, SupportAdminOptions admins) : ISupportService
{
    public async Task<SupportConversation> CreateAsync(Guid actorId, string subject, string body, CancellationToken ct = default)
    {
        var ticket = new SupportTicket(Guid.NewGuid(), actorId, subject, clock.UtcNow);
        var message = new SupportMessage(Guid.NewGuid(), ticket.Id, actorId, body, false, clock.UtcNow);
        await store.AddTicketAsync(ticket, ct); await store.AddMessageAsync(message, ct); await store.SaveChangesAsync(ct);
        return new(ticket, [message]);
    }
    public Task<IReadOnlyList<SupportTicket>> ListAsync(Guid actorId, CancellationToken ct = default) => store.ListForOwnerAsync(actorId, ct);
    public async Task<SupportConversation> GetAsync(Guid actorId, Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await Required(ticketId, ct);
        if (ticket.OwnerUserId != actorId && !admins.UserIds.Contains(actorId)) throw new SupportRuleException(SupportRuleError.NotFound);
        return new(ticket, await store.ListMessagesAsync(ticketId, ct));
    }
    public async Task ReplyAsync(Guid actorId, Guid ticketId, string body, CancellationToken ct = default)
    {
        var ticket = await Required(ticketId, ct);
        if (ticket.OwnerUserId != actorId) throw new SupportRuleException(SupportRuleError.NotFound);
        ticket.EnsureOpen(); await AddMessage(ticketId, actorId, body, false, ct);
    }
    public Task<IReadOnlyList<SupportTicket>> ListAllAsync(Guid administratorId, CancellationToken ct = default)
    { EnsureAdmin(administratorId); return store.ListAllAsync(ct); }
    public async Task ReplyAsStaffAsync(Guid administratorId, Guid ticketId, string body, CancellationToken ct = default)
    { EnsureAdmin(administratorId); var ticket = await Required(ticketId, ct); ticket.EnsureOpen(); await AddMessage(ticketId, administratorId, body, true, ct); }
    public async Task CloseAsync(Guid administratorId, Guid ticketId, CancellationToken ct = default)
    { EnsureAdmin(administratorId); var ticket = await Required(ticketId, ct); ticket.Close(clock.UtcNow); await store.SaveChangesAsync(ct); }
    private async Task<SupportTicket> Required(Guid id, CancellationToken ct) => await store.FindTicketAsync(id, ct) ?? throw new SupportRuleException(SupportRuleError.NotFound);
    private void EnsureAdmin(Guid id) { if (!admins.UserIds.Contains(id)) throw new SupportRuleException(SupportRuleError.Forbidden); }
    private async Task AddMessage(Guid ticketId, Guid authorId, string body, bool staff, CancellationToken ct)
    { await store.AddMessageAsync(new SupportMessage(Guid.NewGuid(), ticketId, authorId, body, staff, clock.UtcNow), ct); await store.SaveChangesAsync(ct); }
}
