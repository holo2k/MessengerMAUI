using Messenger.Application.Accounts;
using Messenger.Application.Support;
using Messenger.Domain.Accounts;
using Messenger.Domain.Common;
using Messenger.Domain.Support;

namespace Messenger.Application.Tests.Support;

public sealed class SupportAndDeletionTests
{
    [Fact]
    public async Task Ticket_is_visible_only_to_owner_and_cannot_be_replied_to_after_close()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var store = new MemorySupportStore();
        var service = new SupportService(store, new FixedClock(), new SupportAdminOptions { UserIds = [admin] });

        var ticket = await service.CreateAsync(owner, "Помощь", "Не работает");

        await Assert.ThrowsAsync<SupportRuleException>(() => service.GetAsync(stranger, ticket.Ticket.Id));
        await service.ReplyAsStaffAsync(admin, ticket.Ticket.Id, "Проверяем");
        await service.CloseAsync(admin, ticket.Ticket.Id);
        await Assert.ThrowsAsync<SupportRuleException>(() => service.ReplyAsync(owner, ticket.Ticket.Id, "Спасибо"));
        Assert.Equal(2, store.Messages.Count);
    }

    [Fact]
    public async Task Deletion_is_scheduled_for_30_days_revokes_sessions_and_can_be_cancelled()
    {
        var userId = Guid.NewGuid();
        var store = new MemoryDeletionStore();
        var service = new AccountDeletionService(store, new FixedClock());

        var request = await service.RequestAsync(userId);

        Assert.Equal(TimeSpan.FromDays(30), request.ExecuteAt - request.RequestedAt);
        Assert.Equal(userId, store.RevokedUserId);
        await service.CancelAsync(userId);
        Assert.NotNull(request.CancelledAt);
    }

    [Fact]
    public async Task Due_deletion_anonymizes_account_but_does_not_remove_messages()
    {
        var userId = Guid.NewGuid();
        var clock = new FixedClock();
        var store = new MemoryDeletionStore();
        store.Requests.Add(new AccountDeletionRequest(Guid.NewGuid(), userId, clock.UtcNow.AddDays(-31)));
        var service = new AccountDeletionService(store, clock);

        var processed = await service.ProcessDueAsync();

        Assert.Equal(1, processed);
        Assert.Equal(userId, store.AnonymizedUserId);
        Assert.True(store.Requests[0].ExecutedAt.HasValue);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class MemorySupportStore : ISupportStore
    {
        public List<SupportTicket> Tickets { get; } = [];
        public List<SupportMessage> Messages { get; } = [];
        public Task AddTicketAsync(SupportTicket ticket, CancellationToken ct) { Tickets.Add(ticket); return Task.CompletedTask; }
        public Task AddMessageAsync(SupportMessage message, CancellationToken ct) { Messages.Add(message); return Task.CompletedTask; }
        public Task<SupportTicket?> FindTicketAsync(Guid id, CancellationToken ct) => Task.FromResult(Tickets.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<SupportTicket>> ListForOwnerAsync(Guid ownerId, CancellationToken ct) => Task.FromResult<IReadOnlyList<SupportTicket>>(Tickets.Where(x => x.OwnerUserId == ownerId).ToArray());
        public Task<IReadOnlyList<SupportTicket>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SupportTicket>>(Tickets.ToArray());
        public Task<IReadOnlyList<SupportMessage>> ListMessagesAsync(Guid ticketId, CancellationToken ct) => Task.FromResult<IReadOnlyList<SupportMessage>>(Messages.Where(x => x.TicketId == ticketId).ToArray());
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class MemoryDeletionStore : IAccountDeletionStore
    {
        public List<AccountDeletionRequest> Requests { get; } = [];
        public Guid? RevokedUserId { get; private set; }
        public Guid? AnonymizedUserId { get; private set; }
        public Task<AccountDeletionRequest?> FindActiveAsync(Guid userId, CancellationToken ct) => Task.FromResult(Requests.SingleOrDefault(x => x.UserId == userId && x.CancelledAt is null && x.ExecutedAt is null));
        public Task AddAsync(AccountDeletionRequest request, CancellationToken ct) { Requests.Add(request); return Task.CompletedTask; }
        public Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken ct) { RevokedUserId = userId; return Task.CompletedTask; }
        public Task<IReadOnlyList<AccountDeletionRequest>> ListDueAsync(DateTimeOffset now, CancellationToken ct) => Task.FromResult<IReadOnlyList<AccountDeletionRequest>>(Requests.Where(x => x.IsDue(now)).ToArray());
        public Task AnonymizeAsync(Guid userId, DateTimeOffset now, CancellationToken ct) { AnonymizedUserId = userId; return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
