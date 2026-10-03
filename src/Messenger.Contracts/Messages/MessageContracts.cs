namespace Messenger.Contracts.Messages;

public sealed record SendMessageRequest(Guid ClientMessageId, string Type, string? Body, IReadOnlyList<Guid>? AttachmentObjectIds = null);
public sealed record EditMessageRequest(string Body);
public sealed record MessageResponse(
    Guid Id,
    Guid ChatId,
    Guid SenderUserId,
    long Sequence,
    Guid ClientMessageId,
    string Type,
    string? Body,
    bool IsPinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt);
public sealed record MessagePageResponse(IReadOnlyList<MessageResponse> Items, string? NextCursor);
