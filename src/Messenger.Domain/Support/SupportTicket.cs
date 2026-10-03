using Messenger.Domain.Common;

namespace Messenger.Domain.Support;

public enum SupportTicketStatus { Open, Closed }
public enum SupportRuleError { NotFound, Forbidden, Closed, Invalid }

public sealed class SupportRuleException(SupportRuleError code) : Exception(code.ToString())
{
    public SupportRuleError Code { get; } = code;
}

public sealed class SupportTicket : Entity
{
    private SupportTicket() { }
    public SupportTicket(Guid id, Guid ownerUserId, string subject, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new SupportRuleException(SupportRuleError.Invalid);
        Id = id; OwnerUserId = ownerUserId; Subject = subject.Trim(); CreatedAt = createdAt;
    }
    public Guid OwnerUserId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public SupportTicketStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public void EnsureOpen() { if (Status == SupportTicketStatus.Closed) throw new SupportRuleException(SupportRuleError.Closed); }
    public void Close(DateTimeOffset now) { EnsureOpen(); Status = SupportTicketStatus.Closed; ClosedAt = now; }
}

public sealed class SupportMessage : Entity
{
    private SupportMessage() { }
    public SupportMessage(Guid id, Guid ticketId, Guid authorUserId, string body, bool isStaff, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new SupportRuleException(SupportRuleError.Invalid);
        Id = id; TicketId = ticketId; AuthorUserId = authorUserId; Body = body.Trim(); IsStaff = isStaff; CreatedAt = createdAt;
    }
    public Guid TicketId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public bool IsStaff { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
