using Messenger.Domain.Common;

namespace Messenger.Domain.Accounts;

public enum AccountDeletionError { NotFound, AlreadyCancelled, AlreadyExecuted }
public sealed class AccountDeletionException(AccountDeletionError code) : Exception(code.ToString())
{
    public AccountDeletionError Code { get; } = code;
}

public sealed class AccountDeletionRequest : Entity
{
    private AccountDeletionRequest() { }
    public AccountDeletionRequest(Guid id, Guid userId, DateTimeOffset requestedAt)
    { Id = id; UserId = userId; RequestedAt = requestedAt; ExecuteAt = requestedAt.AddDays(30); }
    public Guid UserId { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset ExecuteAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? ExecutedAt { get; private set; }
    public bool IsDue(DateTimeOffset now) => CancelledAt is null && ExecutedAt is null && ExecuteAt <= now;
    public void Cancel(DateTimeOffset now)
    {
        if (ExecutedAt.HasValue) throw new AccountDeletionException(AccountDeletionError.AlreadyExecuted);
        if (CancelledAt.HasValue) throw new AccountDeletionException(AccountDeletionError.AlreadyCancelled);
        CancelledAt = now;
    }
    public void MarkExecuted(DateTimeOffset now)
    {
        if (CancelledAt.HasValue) throw new AccountDeletionException(AccountDeletionError.AlreadyCancelled);
        ExecutedAt = now;
    }
}
