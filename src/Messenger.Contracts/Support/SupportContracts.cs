namespace Messenger.Contracts.Support;

public sealed record CreateSupportTicketRequest(string Subject, string Body);
public sealed record SupportReplyRequest(string Body);
public sealed record SupportTicketResponse(Guid Id, Guid OwnerUserId, string Subject, string Status, DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt);
public sealed record SupportMessageResponse(Guid Id, Guid AuthorUserId, string Body, bool IsStaff, DateTimeOffset CreatedAt);
public sealed record SupportConversationResponse(SupportTicketResponse Ticket, IReadOnlyList<SupportMessageResponse> Messages);
public sealed record AccountDeletionResponse(Guid Id, DateTimeOffset RequestedAt, DateTimeOffset ExecuteAt);
